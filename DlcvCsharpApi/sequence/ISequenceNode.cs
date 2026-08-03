using System.Threading.Tasks;

namespace DLCV.SequenceGraph
{
    public interface ISequenceNode
    {
        string Type { get; }
        Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor);
        Task ArmAsync(SequenceGraphExecutor executor, string nodeId);
        Task DisarmAsync();
    }

    public abstract class SequenceNodeBase : ISequenceNode
    {
        public abstract string Type { get; }

        public abstract Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor);

        public virtual Task ArmAsync(SequenceGraphExecutor executor, string nodeId)
        {
            return Task.CompletedTask;
        }

        public virtual Task DisarmAsync()
        {
            return Task.CompletedTask;
        }
    }
}

