using TorchSharp;

namespace ToolUse.Core.RL
{
    public static class TensorExtensions
    {
        public static float[] ToArray_Float(this torch.Tensor tensor)
        {
            return tensor.cpu().data<float>().ToArray();
        }
    }
}
