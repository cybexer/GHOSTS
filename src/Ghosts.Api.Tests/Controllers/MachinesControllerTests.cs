using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ghosts.Api.Controllers.Api;
using Ghosts.Api.Infrastructure.Models;
using Ghosts.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Ghosts.Api.Tests.Controllers;

public class MachinesControllerTests
{
    private readonly Mock<IMachineService> _service = new();

    [Fact]
    public async Task HandlerActivity_ReturnsOk_WithServiceResult()
    {
        var expected = new List<HandlerActivity> { new("Command", 3, 2, []) };
        _service.Setup(s => s.GetHandlerActivity(168, 60, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await new MachinesController(_service.Object).HandlerActivity(168, 60, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(24, 0)]
    public async Task HandlerActivity_ReturnsBadRequest_WhenHoursOrBucketIsNotPositive(int hours, int bucketMinutes)
    {
        var result = await new MachinesController(_service.Object).HandlerActivity(hours, bucketMinutes, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _service.VerifyNoOtherCalls();
    }
}
