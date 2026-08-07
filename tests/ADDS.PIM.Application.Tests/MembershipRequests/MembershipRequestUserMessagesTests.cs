using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Worker;

namespace ADDS.PIM.Application.Tests.MembershipRequests;

public sealed class MembershipRequestUserMessagesTests
{
    [Fact]
    public void ForWorkerResult_ExplainsExistingMembershipWithoutExposingTechnicalDetails()
    {
        var result = new WorkerMembershipDispatchResult(WorkerMembershipDispatchKind.Completed, new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.ExistingMembership, "gdc.example.org", null, "ExistingMembership"), 200, null);

        Assert.Equal("Das Zielkonto ist bereits Mitglied dieser Gruppe. Eine bestehende zeitlich begrenzte oder permanente Mitgliedschaft wird nicht verlängert oder ersetzt.", MembershipRequestUserMessages.ForWorkerResult(result));
    }
}
