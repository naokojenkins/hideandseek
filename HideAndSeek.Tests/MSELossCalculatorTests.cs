using System;
using TorchSharp;
using Xunit;
using HideAndSeek.Core.RL;

namespace HideAndSeek.Tests
{
    public class MSELossCalculatorTests
    {
        [Fact]
        public void Calculates_Elementwise_MSE_And_Finite()
        {
            using var a = torch.tensor(new float[] { 1f, 2f, 3f });
            using var b = torch.tensor(new float[] { 1f, 0f, 5f });
            var calc = new MSELossCalculator();
            using var loss = calc.Calculate(a, b);
            Assert.Equal(new long[] { 3 }, loss.shape);
            // (1-1)^2=0, (2-0)^2=4, (3-5)^2=4
            Assert.True(torch.allclose(loss, torch.tensor(new float[] { 0f, 4f, 4f }), rtol:1e-6, atol:1e-6));
        }

        [Fact]
        public void Shape_Mismatch_ShouldThrow()
        {
            using var a = torch.randn(new long[] { 2, 3 });
            using var b = torch.randn(new long[] { 3, 2 });
            var calc = new MSELossCalculator();
            Assert.Throws<ArgumentException>(() => calc.Calculate(a, b));
        }

        [Fact]
        public void NaN_In_Input_ShouldThrow()
        {
            using var a = torch.tensor(new float[] { 1f, float.NaN });
            using var b = torch.tensor(new float[] { 1f, 2f });
            var calc = new MSELossCalculator();
            Assert.Throws<InvalidOperationException>(() => calc.Calculate(a, b));
        }
    }
}
