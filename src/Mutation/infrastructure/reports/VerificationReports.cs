using Mutation.Application;
using Mutation.Domain;

namespace Mutation.Infrastructure.Reports;

/// <summary>file 書き出しによる IVerificationReports の adapter</summary>
public sealed class VerificationReports : IVerificationReports
{
    /// <inheritdoc />
    public void Write(MutationRunResult result, string projectRoot, string reportsDirectory) =>
        ReportBundle.Write(result, projectRoot, reportsDirectory);
}
