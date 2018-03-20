using Respawn.Graph;

namespace Respawn
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public interface IDbAdapter
    {
        string BuildTableCommandText(Checkpoint checkpoint);
        string BuildRelationshipCommandText(Checkpoint checkpoint);
        /// <summary>
        /// Builds one or more delete commands. Some databases can accept multiple commands in one string. The adapters for those 
        /// databases will typically just return all commands in the first string seperated by a database specific seperator.
        /// Other Adapters like Firebird have to return each command in a seperate string.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        IEnumerable<string> BuildDeleteCommandText(GraphBuilder builder);
        string BuildReseedSql(IEnumerable<Table> tablesToDelete);
    }

    public static class DbAdapter
    {
        public static readonly IDbAdapter SqlServer = new SqlServerDbAdapter();
        public static readonly IDbAdapter Postgres = new PostgresDbAdapter();
        public static readonly IDbAdapter MySql = new MySqlAdapter();
        public static readonly IDbAdapter Oracle = new OracleDbAdapter();

        private class SqlServerDbAdapter : IDbAdapter
        {
            private const char QuoteCharacter = '"';

            public string BuildTableCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
select s.name, t.name
from sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE 1=1";

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(t => $"N'{t}'"));

                    commandText += " AND t.name NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(t => $"N'{t}'"));

                    commandText += " AND s.name NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(t => $"N'{t}'"));

                    commandText += " AND s.name IN (" + args + ")";
                }

                return commandText;
            }

            public string BuildRelationshipCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
select
   fk_schema.name, so_fk.name,
   pk_schema.name, so_pk.name,
   sfk.name
from
sys.foreign_keys sfk
	inner join sys.objects so_pk on sfk.referenced_object_id = so_pk.object_id
	inner join sys.schemas pk_schema on so_pk.schema_id = pk_schema.schema_id
	inner join sys.objects so_fk on sfk.parent_object_id = so_fk.object_id			
	inner join sys.schemas fk_schema on so_fk.schema_id = fk_schema.schema_id
where 1=1";

                if (checkpoint.TablesToIgnore != null && checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(t => $"N'{t}'"));

                    commandText += " AND so_pk.name NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude != null && checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(t => $"N'{t}'"));

                    commandText += " AND pk_schema.name NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude != null && checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(t => $"N'{t}'"));

                    commandText += " AND pk_schema.name IN (" + args + ")";
                }

                return commandText;
            }

            public string BuildDeleteCommandText(GraphBuilder graph)
            {
                var builder = new StringBuilder();

                foreach (var table in graph.CyclicalTableRelationships.Select(rel => rel.ParentTable))
                {
                    builder.AppendLine($"ALTER TABLE {table.GetFullName(QuoteCharacter)} NOCHECK CONSTRAINT ALL;");
                }
                foreach (var table in graph.ToDelete)
                {
                    builder.AppendLine($"DELETE {table.GetFullName(QuoteCharacter)};");
                }
                foreach (var table in graph.CyclicalTableRelationships.Select(rel => rel.ParentTable))
                {
                    builder.AppendLine($"ALTER TABLE {table.GetFullName(QuoteCharacter)} WITH CHECK CHECK CONSTRAINT ALL;");
                }
                yield return builder.ToString();
            }

            public string BuildReseedSql(IEnumerable<Table> tablesToDelete)
            {
                     string sql =
                        "DECLARE @Schema sysname = N''                                                                                                     			\n" +
                        "DECLARE @TableName sysname = N''                                                                                                  			\n" +
                        "DECLARE @ColumnName sysname = N''                                                                                                 			\n" +
                        "DECLARE @DoReseed sql_variant = 0																											\n" +
                        "DECLARE @NewSeed bigint = 0                                                                                                       			\n" +
                        "DECLARE @IdentityInitialSeedValue int = 0                                                                                                  \n" +
                        "DECLARE @SQL nvarchar(4000) = N''                                                                                                 			\n" +
                        "                                                                                                                                  			\n" +
                        "-- find all non-system tables and load into a cursor                                                                              			\n" +
                        "DECLARE IdentityTables CURSOR FAST_FORWARD                                                                                        			\n" +
                        "FOR                                                                                                                               			\n" +
                        "    SELECT  OBJECT_SCHEMA_NAME(t.object_id, db_id()) as schemaName,                                                                        \n" +
                        "            t.name as tableName,                                                                                                           \n" +
                        "            c.name as columnName,                                                                                                          \n" +
                        "            ic.last_value,                                                                                                                 \n" +
                        "            IDENT_SEED(t.name) as identityInitialSeedValue                                                                                 \n" +
                        "     FROM sys.tables t 																										            \n" +
                        "		JOIN sys.columns c ON t.object_id=c.object_id      																                	\n" +
                        "		JOIN sys.identity_columns ic on ic.object_id = c.object_id  												                		\n" +
                        "    WHERE c.is_identity = 1                                                                                    				            \n" +
                       $"    AND OBJECT_SCHEMA_NAME(t.object_id, db_id()) + '.' + t.name in ('{string.Join("', '", tablesToDelete)}')                              \n" +
                        "OPEN IdentityTables                                                                                                               			\n" +
                        "FETCH NEXT FROM IdentityTables INTO @Schema, @TableName, @ColumnName, @DoReseed, @IdentityInitialSeedValue                                 \n" +
                        "WHILE @@FETCH_STATUS = 0                                                                                                          			\n" +
                        "    BEGIN                                                                                                                         			\n" +
                        "     -- reseed the identity only on tables that actually have had a value, otherwise next value will be off-by-one   			            \n" +
                        "     -- https://stackoverflow.com/questions/472578/dbcc-checkident-sets-identity-to-0                                                      \n" +
                        "        if (@DoReseed is not null)                                                                                                         \n" +
                        "           SET @SQL = N'DBCC CHECKIDENT(''' +  @Schema + '.' + @TableName + ''', RESEED, ' + Convert(varchar(max), @IdentityInitialSeedValue - 1) + ')' \n" +
                        "        else                                                                                                                               \n" +
                        "           SET @SQL = null	                                                                                                                \n" +
                        "        if (@sql is not null) EXECUTE (@SQL)  																								\n" +
                        "		--Print isnull(@sql,  @Schema + '.' + @TableName + ' null')                                                                         \n" +
                        "        FETCH NEXT FROM IdentityTables INTO  @Schema, @TableName, @ColumnName  , @DoReseed, @IdentityInitialSeedValue                      \n" +
                        "    END                                                                                                                           			\n" +
                        " DEALLOCATE IdentityTables                                                                                                                 \n";

                return sql;
            }
        }

        private class PostgresDbAdapter : IDbAdapter
        {
            private const char QuoteCharacter = '"';

            public string BuildTableCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
select TABLE_SCHEMA, TABLE_NAME
from INFORMATION_SCHEMA.TABLES
where TABLE_TYPE = 'BASE TABLE'"
        ;

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(t => $"'{t}'"));

                    commandText += " AND TABLE_NAME NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(t => $"'{t}'"));

                    commandText += " AND TABLE_SCHEMA NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(t => $"'{t}'"));

                    commandText += " AND TABLE_SCHEMA IN (" + args + ")";
                }

                return commandText;
            }

            public string BuildRelationshipCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
select tc.table_schema, tc.table_name, ctu.table_schema, ctu.table_name, rc.constraint_name
from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
inner join INFORMATION_SCHEMA.CONSTRAINT_TABLE_USAGE ctu ON rc.constraint_name = ctu.constraint_name
inner join INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc ON rc.constraint_name = tc.constraint_name
where 1=1";

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(t => $"'{t}'"));

                    commandText += " AND tc.TABLE_NAME NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(t => $"'{t}'"));

                    commandText += " AND tc.TABLE_SCHEMA NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(t => $"'{t}'"));

                    commandText += " AND tc.TABLE_SCHEMA IN (" + args + ")";
                }

                return commandText;
            }

            public IEnumerable<string> BuildDeleteCommandText(GraphBuilder graph)
            {
                var builder = new StringBuilder();

                foreach (var table in graph.CyclicalTableRelationships.Select(rel => rel.ParentTable))
                {
                    builder.AppendLine($"ALTER TABLE {table.GetFullName(QuoteCharacter)} DISABLE TRIGGER ALL;");
                }
                foreach (var table in graph.ToDelete)
                {
                    builder.AppendLine($"truncate table {table.GetFullName(QuoteCharacter)} cascade;");
                }
                foreach (var table in graph.CyclicalTableRelationships.Select(rel => rel.ParentTable))
                {
                    builder.AppendLine($"ALTER TABLE {table.GetFullName(QuoteCharacter)} ENABLE TRIGGER ALL;");
                }

                yield return builder.ToString();
            }

            public string BuildReseedSql(IEnumerable<Table> tablesToDelete)
            {
                throw new System.NotImplementedException();
            }
        }

        private class MySqlAdapter : IDbAdapter
        {
            private const char QuoteCharacter = '`';

            public string BuildTableCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
SELECT t.TABLE_SCHEMA, t.TABLE_NAME
FROM
    information_schema.tables AS t
WHERE
    table_type = 'BASE TABLE'
    AND TABLE_SCHEMA NOT IN ('mysql' , 'performance_schema')";

                if (checkpoint.TablesToIgnore != null && checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(t => $"'{t}'"));

                    commandText += " AND t.TABLE_NAME NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude != null && checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(t => $"'{t}'"));

                    commandText += " AND t.TABLE_SCHEMA NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude != null && checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(t => $"'{t}'"));

                    commandText += " AND t.TABLE_SCHEMA IN (" + args + ")";
                }

                return commandText;
            }

            public string BuildRelationshipCommandText(Checkpoint checkpoint)
            {
                var commandText = @"
SELECT 
    CONSTRAINT_SCHEMA, 
    TABLE_NAME,
    UNIQUE_CONSTRAINT_SCHEMA, 
    REFERENCED_TABLE_NAME, 
    CONSTRAINT_NAME
FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS";

                var whereText = new List<string>();

                if (checkpoint.TablesToIgnore != null && checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(t => $"'{t}'"));
                    whereText.Add("TABLE_NAME NOT IN (" + args + ")");
                }
                if (checkpoint.SchemasToExclude != null && checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(t => $"'{t}'"));
                    whereText.Add("CONSTRAINT_SCHEMA NOT IN (" + args + ")");
                }
                else if (checkpoint.SchemasToInclude != null && checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(t => $"'{t}'"));
                    whereText.Add("CONSTRAINT_SCHEMA IN (" + args + ")");
                }

                if (whereText.Any())
                    commandText += $" WHERE {string.Join(" AND ", whereText.ToArray())}";
                return commandText;
            }

            public IEnumerable<string> BuildDeleteCommandText(GraphBuilder graph)
            {
                var builder = new StringBuilder();

                builder.AppendLine("SET FOREIGN_KEY_CHECKS=0;");
                foreach (var table in graph.ToDelete)
                {
                    builder.AppendLine($"DELETE FROM {table.GetFullName(QuoteCharacter)};");
                }
                builder.AppendLine("SET FOREIGN_KEY_CHECKS=1;");

                yield return builder.ToString();
            }

            public string BuildReseedSql(IEnumerable<Table> tablesToDelete)
            {
                throw new System.NotImplementedException();
            }
        }

        private class OracleDbAdapter : IDbAdapter
        {
            private const char QuoteCharacter = '"';

            public string BuildTableCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
select OWNER, TABLE_NAME
from ALL_TABLES
where 1=1 "
        ;

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(table => $"'{table}'").ToArray());

                    commandText += " AND TABLE_NAME NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(schema => $"'{schema}'").ToArray());

                    commandText += " AND OWNER NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(schema => $"'{schema}'").ToArray());

                    commandText += " AND OWNER IN (" + args + ")";
                }

                return commandText;
            }

            public string BuildRelationshipCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
select a.owner as table_schema,a.table_name, b.owner as table_schema ,b.table_name, a.constraint_name
from all_CONSTRAINTS     a
         inner join all_CONSTRAINTS b on a.r_constraint_name=b.constraint_name 
         where a.constraint_type in ('P','R')";

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(s => $"'{s}'").ToArray());

                    commandText += " AND a.TABLE_NAME NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(s => $"'{s}'").ToArray());

                    commandText += " AND a.OWNER NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(s => $"'{s}'").ToArray());

                    commandText += " AND a.OWNER IN (" + args + ")";
                }

                return commandText;
            }

            public IEnumerable<string> BuildDeleteCommandText(GraphBuilder graph)
            {
                var deleteSql = string.Join("\n", BuildCommands(graph));
                yield return $"BEGIN\n{deleteSql}\nEND;";
            }

            private IEnumerable<string> BuildCommands(GraphBuilder graph)
            {
                foreach (var rel in graph.CyclicalTableRelationships)
                {
                    yield return $"EXECUTE IMMEDIATE 'ALTER TABLE {rel.ParentTable.GetFullName(QuoteCharacter)} DISABLE CONSTRAINT {QuoteCharacter}{rel.Name}{QuoteCharacter}';";
                }
                foreach (var table in graph.ToDelete)
                {
                    yield return $"EXECUTE IMMEDIATE 'delete from {table.GetFullName(QuoteCharacter)}';";
                }
                foreach (var rel in graph.CyclicalTableRelationships)
                {
                    yield return $"EXECUTE IMMEDIATE 'ALTER TABLE {rel.ParentTable.GetFullName(QuoteCharacter)} ENABLE CONSTRAINT {QuoteCharacter}{rel.Name}{QuoteCharacter}';";
                }
            }
            public string BuildReseedSql(IEnumerable<Table> tablesToDelete)
            {
                throw new System.NotImplementedException();
            }
        }

        private class FirebirdDbAdapter : IDbAdapter
        {
            private const char QuoteCharacter = '"';

            public string BuildTableCommandText(Checkpoint checkpoint)
            {
                //We could use rdb$owner_name as the first column, but Firebird does not support 
                //prefixing with owner when selecting a column
                string commandText = 
@"select null, rdb$relation_name
from rdb$relations
where rdb$view_blr is null 
and (rdb$system_flag is null or rdb$system_flag = 0)";

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(table => $"'{table}'").ToArray());

                    commandText += " AND rdb$relation_name NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    //One could argue against having SchemasToExclude and SchemasToInclude in the FirebirdAdapter because
                    //it is a meaningless filtering on the owner_name (schemas does not exist in Firebird).  
                    //I keep it here because I want the adapter to behave the same way as the other adapters, 
                    //and because some might find it useful to use the owner_name for namespacing
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(schema => $"'{schema}'").ToArray());

                    commandText += " AND rdb$owner_name NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(schema => $"'{schema}'").ToArray());

                    commandText += " AND rdb$owner_name IN (" + args + ")";
                }

                return commandText;
            }

            public string BuildRelationshipCommandText(Checkpoint checkpoint)
            {
                string commandText = @"
SELECT
    NULL,
    drc.rdb$relation_name as TableName,
    NULL,
    mrc.rdb$relation_name as RefTableName,
    drc.rdb$constraint_name as ConstraintName

FROM
    rdb$relation_constraints drc
    JOIN rdb$index_segments dis ON drc.rdb$index_name = dis.rdb$index_name 
    JOIN rdb$ref_constraints ON drc.rdb$constraint_name = rdb$ref_constraints.rdb$constraint_name
    JOIN rdb$relation_constraints mrc ON rdb$ref_constraints.rdb$const_name_uq = mrc.rdb$constraint_name
    JOIN rdb$index_segments mis ON mrc.rdb$index_name = mis.rdb$index_name 
    JOIN (select r.rdb$relation_name, r.rdb$owner_name from rdb$relations r) ot on ot.rdb$relation_name = drc.rdb$relation_name

WHERE
    drc.rdb$constraint_type = 'FOREIGN KEY'
";

                if (checkpoint.TablesToIgnore.Any())
                {
                    var args = string.Join(",", checkpoint.TablesToIgnore.Select(s => $"'{s}'").ToArray());

                    commandText += " AND drc.rdb$relation_name NOT IN (" + args + ")";
                }
                if (checkpoint.SchemasToExclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToExclude.Select(s => $"'{s}'").ToArray());

                    commandText += " AND ot.rdb$owner_name NOT IN (" + args + ")";
                }
                else if (checkpoint.SchemasToInclude.Any())
                {
                    var args = string.Join(",", checkpoint.SchemasToInclude.Select(s => $"'{s}'").ToArray());

                    commandText += " AND ot.rdb$owner_name IN (" + args + ")";
                }

                return commandText;
            }

            public IEnumerable<string> BuildDeleteCommandText(GraphBuilder graph)
            {
                return BuildCommands(graph);
            }

            private IEnumerable<string> BuildCommands(GraphBuilder graph)
            {
                foreach (var rel in graph.CyclicalTableRelationships)
                {
                    yield return $"EXECUTE IMMEDIATE 'ALTER TABLE {rel.ParentTable.GetFullName(QuoteCharacter)} DISABLE CONSTRAINT {QuoteCharacter}{rel.Name}{QuoteCharacter}';";
                }
                foreach (var table in graph.ToDelete)
                {
                    yield return $"EXECUTE IMMEDIATE 'delete from {table.GetFullName(QuoteCharacter)}';";
                }
                foreach (var rel in graph.CyclicalTableRelationships)
                {
                    yield return $"EXECUTE IMMEDIATE 'ALTER TABLE {rel.ParentTable.GetFullName(QuoteCharacter)} ENABLE CONSTRAINT {QuoteCharacter}{rel.Name}{QuoteCharacter}';";
                }
            }
            public string BuildReseedSql(IEnumerable<Table> tablesToDelete)
            {
                throw new System.NotImplementedException();
            }
        }
    }
}
