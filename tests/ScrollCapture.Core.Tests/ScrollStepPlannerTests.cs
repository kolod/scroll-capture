using ScrollCapture.Core.Stitching;
using Xunit;

namespace ScrollCapture.Core.Tests
{
    public class ScrollStepPlannerTests
    {
        [Fact]
        public void FirstStep_UsesTheInitialUnits()
        {
            var planner = new ScrollStepPlanner(2, 40);

            Assert.Equal(2, planner.Next(800));
            Assert.Null(planner.ExpectedShift(2));
        }

        [Fact]
        public void LaterSteps_AimAtTheTargetShareOfTheBand()
        {
            var planner = new ScrollStepPlanner(1, 40);
            planner.Record(1, 50);

            Assert.Equal(9, planner.Next(800)); // 0.6 × 800 / 50 = 9.6
            Assert.Equal(450, planner.ExpectedShift(9));
        }

        [Fact]
        public void HugeUnits_StillScrollOneUnit()
        {
            var planner = new ScrollStepPlanner(1, 40);
            planner.Record(1, 2000);

            Assert.Equal(1, planner.Next(600));
        }

        [Fact]
        public void TooFar_HalvesTheLimit_UntilOneUnit()
        {
            var planner = new ScrollStepPlanner(1, 40);
            planner.Record(1, 10);
            Assert.Equal(40, planner.Next(1000));

            Assert.True(planner.TooFar(40));
            Assert.Equal(20, planner.Next(1000));
            Assert.False(planner.TooFar(1));
        }
    }
}
