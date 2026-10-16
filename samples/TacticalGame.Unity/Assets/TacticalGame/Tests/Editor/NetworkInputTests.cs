using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
using UnityEngine;
namespace Advanture.TacticalGame.Tests
{
    public sealed class NetworkInputTests
    {
        [Test] public void ForwardUsesCapturedCameraDirection()
        {
            var input=InputQuantizer.Create(Vector2.up,90,-20);
            Assert.That(input.MoveX,Is.EqualTo(1000));Assert.That(input.MoveZ,Is.EqualTo(0));
            Assert.That(input.Yaw,Is.EqualTo(9000));Assert.That(input.Pitch,Is.EqualTo(-2000));
        }
        [Test] public void AllAnglesAndDiagonalInputsAreCanonical()
        {
            for(int yaw=-720;yaw<720;yaw++)
            {
                var input=InputQuantizer.Create(Vector2.one,yaw+.009f,120);
                Assert.DoesNotThrow(()=>TacticalInput.Decode(input.Encode()));
                Assert.That((long)input.MoveX*input.MoveX+(long)input.MoveZ*input.MoveZ,Is.LessThanOrEqualTo(1000000));
            }
        }
    }
}
