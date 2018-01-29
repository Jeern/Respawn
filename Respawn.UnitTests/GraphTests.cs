﻿using System;
using System.Collections.Generic;
using System.Linq;
using Respawn.Graph;
using Shouldly;
using Xunit;

namespace Respawn.UnitTests
{
    public class GraphTests
    {
        [Fact]
        public void ShouldConstructDeleteListWithOneTable()
        {
            var a = new Table("dbo", "A");
            var builder = new GraphBuilder
                (new HashSet<Table>(new[] {a}), 
                new HashSet<Relationship>(Enumerable.Empty<Relationship>()));
            
            builder.ToDelete.ToList().ShouldBe(new [] {a});
        }

        [Fact]
        public void ShouldConstructDeleteListWithMultipleUnrelatedTables()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var builder = new GraphBuilder(new HashSet<Table>(new[] {a, b}), new HashSet<Relationship>(Enumerable.Empty<Relationship>()));
            
            builder.ToDelete.ShouldBe(new [] {a, b});
        }

        [Fact]
        public void ShouldConstructDeleteListWithSingleRelatedTable()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var builder = new GraphBuilder(new HashSet<Table>(new[] {a, b}), new HashSet<Relationship>(new[] {aToB}));
            
            builder.ToDelete.ShouldBe(new [] {b, a});
        }

        [Fact]
        public void ShouldConstructDeleteListWithMoreComplexGraph()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToC = new Relationship("dbo", "B", "dbo", "C", "B.C");
            var builder = new GraphBuilder(new HashSet<Table>(new[] {a, b, c}), new HashSet<Relationship>(new[] {aToB, bToC}));

            builder.ToDelete.ShouldBe(new[] { c, b, a });
        }

        [Fact]
        public void ShouldConstructDeleteListWithMultipleRelationships()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToC = new Relationship("dbo", "B", "dbo", "C", "B.C");
            var aToC = new Relationship("dbo", "A", "dbo", "C", "A.C");
            var builder = new GraphBuilder(new HashSet<Table>(new[] {a, b, c}), new HashSet<Relationship>(new[] {aToB, bToC, aToC}));

            builder.ToDelete.ShouldBe(new[] { c, b, a });
        }

        [Fact]
        public void ShouldConstructDeleteListWithMultipleEquivalentRelationships()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToC = new Relationship("dbo", "B", "dbo", "C", "B.C");
            var aToC1 = new Relationship("dbo", "A", "dbo", "C", "A.C1");
            var aToC2 = new Relationship("dbo", "A", "dbo", "C", "A.C2");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b, c }), new HashSet<Relationship>(new[] { aToB, bToC, aToC1, aToC2 }));

            builder.ToDelete.ShouldBe(new[] { c, b, a });
        }

        [Fact]
        public void ShouldConstructDeleteListWithMultipleDisparateRelationships()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var d = new Table("dbo", "D");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var cToD = new Relationship("dbo", "C", "dbo", "D", "C.D");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b, c, d }), new HashSet<Relationship>(new[] {aToB, cToD}));

            builder.ToDelete.ShouldBe(new[] {b, a, d, c});
        }

        [Fact]
        public void ShouldRemoveCycles()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToA = new Relationship("dbo", "B", "dbo", "A", "B.A");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b }), new HashSet<Relationship>(new[] {aToB, bToA}));

            builder.ToDelete.ShouldBeEmpty();
            builder.CyclicalTables.ShouldBe(new[] { a, b });
            builder.CyclicalTableRelationships.ShouldBe(new[] { aToB, bToA });
            builder.CyclicalTableForeignKeyTables.ShouldBeEmpty();
        }

        [Fact]
        public void ShouldIgnoreSelfReferences()
        {
            var a = new Table("dbo", "A");
            var aToA = new Relationship("dbo", "A", "dbo", "A", "A.A");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a }), new HashSet<Relationship>(new[] {aToA}));

            builder.ToDelete.ShouldBe(new[] {a});
            builder.CyclicalTables.ShouldBeEmpty();
            builder.CyclicalTableRelationships.ShouldBeEmpty();
            builder.CyclicalTableForeignKeyTables.ShouldBeEmpty();
        }

        [Fact]
        public void ShouldRemoveCyclesExcludingNormalRelationships()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var d = new Table("dbo", "D");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToA = new Relationship("dbo", "B", "dbo", "A", "B.A");
            var cToD = new Relationship("dbo", "C", "dbo", "D", "C.D");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b, c, d }), new HashSet<Relationship>(new[] { aToB, bToA, cToD }));

            builder.ToDelete.ShouldBe(new[] { d, c });
            builder.CyclicalTables.ShouldBe(new[] { a, b });
            builder.CyclicalTableRelationships.ShouldBe(new[] { aToB, bToA });
            builder.CyclicalTableForeignKeyTables.ShouldBeEmpty();
        }

        [Fact]
        public void ShouldRemoveCyclesWithoutRemovingStart()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToC = new Relationship("dbo", "B", "dbo", "C", "B.C");
            var cToB = new Relationship("dbo", "C", "dbo", "B", "C.B");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b, c }), new HashSet<Relationship>(new[] { aToB, bToC, cToB }));

            builder.ToDelete.ShouldBe(new[] { a });
            builder.CyclicalTables.ShouldBe(new[] { b, c });
            builder.CyclicalTableRelationships.ShouldBe(new[] { aToB, bToC, cToB });
            builder.CyclicalTableForeignKeyTables.ShouldBeEmpty();
        }

        [Fact]
        public void ShouldIsolateCycleRelationships()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var d = new Table("dbo", "D");
            var aToB = new Relationship("dbo", "A", "dbo", "B", "A.B");
            var bToC = new Relationship("dbo", "B", "dbo", "C", "B.C");
            var cToD = new Relationship("dbo", "C", "dbo", "D", "C.D");
            var dToC = new Relationship("dbo", "D", "dbo", "C", "D.C");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b, c, d }), new HashSet<Relationship>(new[] { aToB, bToC, cToD, dToC }));

            builder.ToDelete.ShouldBe(new[] { b, a });
            builder.CyclicalTables.ShouldBe(new[] { c, d });
            builder.CyclicalTableRelationships.ShouldBe(new[] { bToC, cToD, dToC });
            builder.CyclicalTableForeignKeyTables.ShouldBeEmpty();
        }

        [Fact]
        public void ShouldFindCyclicRelationships()
        {
            var a = new Table("dbo", "A");
            var b = new Table("dbo", "B");
            var c = new Table("dbo", "C");
            var d = new Table("dbo", "D");
            var e = new Table("dbo", "E");
            var f = new Table("dbo", "F");
            var aToB = new Relationship("dbo", "B", "dbo", "A", "A.B");
            var bToA = new Relationship("dbo", "A", "dbo", "B", "B.A");
            var bToC = new Relationship("dbo", "C", "dbo", "B", "B.C");
            var bToD = new Relationship("dbo", "D", "dbo", "B", "B.D");
            var cToD = new Relationship("dbo", "D", "dbo", "C", "C.D");
            var eToA = new Relationship("dbo", "A", "dbo", "E", "E.A");
            var fToB = new Relationship("dbo", "B", "dbo", "F", "F.B");
            var builder = new GraphBuilder(new HashSet<Table>(new[] { a, b, c, d, e, f }), new HashSet<Relationship>(new[] { aToB, bToC, cToD, bToA, bToD, eToA, fToB }));

            builder.ToDelete.ShouldBe(new[] { c, d, e, f });
            builder.CyclicalTables.ShouldBe(new[] { a, b });
            builder.CyclicalTableRelationships.ShouldBe(new[] { aToB, bToC, bToA, bToD, eToA, fToB });
            builder.CyclicalTableForeignKeyTables.ShouldBe(new[] {e, f});
        }
    }
}