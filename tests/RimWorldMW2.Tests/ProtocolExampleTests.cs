using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorldMW2;
using Xunit;

namespace RimWorldMW2.Tests
{
    /// <summary>
    /// protocol/examples.ndjson read by the mod's own parser, the C# half of the Rust
    /// examples_round_trip test.
    /// </summary>
    public class ProtocolExampleTests
    {
        private static IEnumerable<string> Lines() =>
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "examples.ndjson"))
                .Where(l => l.Trim().Length > 0 && !l.StartsWith("//"));

        public static IEnumerable<object[]> Examples() => Lines().Select(l => new object[] { l });

        private static IEnumerable<JsonLine> OfType(string type) =>
            Lines().Select(JsonLine.Parse).Where(m => m?.Type == type);

        [Theory]
        [MemberData(nameof(Examples))]
        public void EveryExampleParses(string line)
        {
            JsonLine message = JsonLine.Parse(line);
            Assert.NotNull(message);
            Assert.False(string.IsNullOrEmpty(message.Type), "no \"t\" in " + line);
        }

        // The messages IW4L sends that BridgeGameComponent.Handle acts on, checked for the fields
        // and shapes Handle needs. If one fails, the example and the handler no longer agree.

        [Fact]
        public void PlayerMoveHasPawnAndPosition()
        {
            Assert.All(Require("player_move"), m =>
            {
                Assert.NotNull(m.Str("pawn"));
                Assert.NotNull(m.Num("x"));
                Assert.NotNull(m.Num("z"));
                Assert.NotNull(m.Num("yaw"));
            });
        }

        [Fact]
        public void PlayerShotHasPawnAndThreeNumberEnds()
        {
            Assert.All(Require("player_shot"), m =>
            {
                Assert.NotNull(m.Str("pawn"));
                Assert.Equal(3, m.Nums("from")?.Length);
                Assert.Equal(3, m.Nums("to")?.Length);
            });
        }

        [Fact]
        public void PlayerThrowHasPawnAndGroundPoint()
        {
            Assert.All(Require("player_throw"), m =>
            {
                Assert.NotNull(m.Str("pawn"));
                Assert.Equal(2, m.Nums("to")?.Length);
            });
        }

        [Fact]
        public void SpectateHasPosition()
        {
            Assert.All(Require("spectate"), m =>
            {
                Assert.NotNull(m.Num("x"));
                Assert.NotNull(m.Num("z"));
            });
        }

        [Fact]
        public void PawnMessagesNameTheirPawnAndTarget()
        {
            Assert.All(Require("use"), m => Assert.NotNull(m.Str("pawn")));
            Assert.All(Require("player_struck"), m =>
            {
                Assert.NotNull(m.Str("pawn"));
                Assert.NotNull(m.Str("shot"));
            });
            Assert.All(Require("loot_take").Concat(Require("loot_drop")), m =>
            {
                Assert.NotNull(m.Str("pawn"));
                Assert.NotNull(m.Str("item"));
            });
        }

        [Fact]
        public void MenuOpenIsABareBoolean()
        {
            // Handle compares the raw text with "true", so a quoted or numeric value would never open it.
            Assert.All(Require("menu"), m => Assert.Contains(m.Str("open"), new[] { "true", "false" }));
        }

        [Fact]
        public void RimworldShownAndHelloCarryWindowHandles()
        {
            Assert.All(Require("rimworld_shown"), m => Assert.NotNull(m.Str("back")));
            Assert.Contains(OfType("hello"), m => m.Str("side") == "iw4l" && m.Str("window") != null);
        }

        private static List<JsonLine> Require(string type)
        {
            List<JsonLine> found = OfType(type).ToList();
            Assert.True(found.Count > 0, $"protocol/examples.ndjson has no \"{type}\" message");
            return found;
        }
    }
}
