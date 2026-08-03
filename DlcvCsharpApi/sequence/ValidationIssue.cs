using System;
using System.Collections.Generic;
using System.Text;

namespace DLCV.SequenceGraph
{
    public class ValidationIssue
    {
        public string NodeId { get; set; }
        public string Prop { get; set; }
        public string Message { get; set; }

        public ValidationIssue(string nodeId, string prop, string message)
        {
            NodeId = nodeId;
            Prop = prop;
            Message = message;
        }
    }

    public class SequenceGraphValidationException : Exception
    {
        public IList<ValidationIssue> Issues { get; private set; }

        public SequenceGraphValidationException(IList<ValidationIssue> issues)
            : base(Format(issues))
        {
            Issues = issues;
        }

        private static string Format(IList<ValidationIssue> issues)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < issues.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                var issue = issues[i];
                sb.Append(string.IsNullOrEmpty(issue.NodeId) ? "?" : issue.NodeId);
                sb.Append(": ");
                sb.Append(issue.Message);
            }
            return sb.ToString();
        }
    }
}

