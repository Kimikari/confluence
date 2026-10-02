using System;
using System.Linq;
using Confluence.Casting;

namespace Confluence.Casting.Tests.Input
{
    /// <summary>
    /// The one shared input flag set is serialized by value (Net), so every bit is part of the wire format. These
    /// values must never change; a new action takes the next free bit and is added here.
    /// </summary>
    public class InputButtonsTests
    {
        [Theory]
        [InlineData(InputButtons.Primary, 0)]
        [InlineData(InputButtons.Guard, 1)]
        [InlineData(InputButtons.Sprint, 2)]
        [InlineData(InputButtons.Jump, 3)]
        [InlineData(InputButtons.Crouch, 4)]
        [InlineData(InputButtons.Dodge, 5)]
        [InlineData(InputButtons.Melee, 6)]
        [InlineData(InputButtons.Signature, 7)]
        [InlineData(InputButtons.ActiveItem, 8)]
        [InlineData(InputButtons.Consumable, 9)]
        [InlineData(InputButtons.Recast, 10)]
        [InlineData(InputButtons.CastModifier, 11)]
        [InlineData(InputButtons.ModifierA, 12)]
        [InlineData(InputButtons.ModifierB, 13)]
        [InlineData(InputButtons.MoveForward, 14)]
        [InlineData(InputButtons.MoveBack, 15)]
        [InlineData(InputButtons.MoveLeft, 16)]
        [InlineData(InputButtons.MoveRight, 17)]
        [InlineData(InputButtons.SwapFocus, 18)]
        public void Every_Action_Keeps_Its_Wire_Bit(InputButtons button, int bit)
        {
            Assert.Equal(1u << bit, (uint)button);
        }

        [Fact]
        public void Single_Bit_Actions_Are_Exactly_The_Pinned_Set_And_All_Covers_Them()
        {
            var singles = Enum.GetValues(typeof(InputButtons)).Cast<InputButtons>()
                .Where(b => b != InputButtons.None && ((uint)b & ((uint)b - 1u)) == 0u)
                .ToArray();
            Assert.Equal(19, singles.Length);
            uint union = singles.Aggregate(0u, (acc, b) => acc | (uint)b);
            Assert.Equal((uint)InputButtons.All, union);
            Assert.Equal(InputButtons.MoveForward | InputButtons.MoveBack | InputButtons.MoveLeft | InputButtons.MoveRight, InputButtons.MovementKeys);
        }

        [Fact]
        public void Cancelling_Actions_Are_Guard_Dodge_And_Melee()
        {
            // spec L474: no wizard overrides dodge, guard, or common melee.
            Assert.Equal(InputButtons.Guard | InputButtons.Dodge | InputButtons.Melee, CastingRules.CancellingActions);
        }
    }
}
