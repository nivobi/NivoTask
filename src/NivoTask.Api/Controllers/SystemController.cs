using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NivoTask.Api.Services;
using NivoTask.Shared.Dtos.System;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api/system")]
[Authorize]
public class SystemController : ControllerBase
{
    private readonly UpdateService _update;

    public SystemController(UpdateService update) => _update = update;

    [HttpGet("version")]
    [AllowAnonymous]
    public ActionResult<VersionInfoResponse> GetVersion() => Ok(_update.GetVersionInfo());

    [HttpGet("update-check")]
    public async Task<ActionResult<UpdateCheckResponse>> CheckUpdate([FromQuery] bool refresh = false, CancellationToken ct = default)
        => Ok(await _update.CheckAsync(useCache: !refresh, ct));

    [HttpPost("update")]
    public async Task<ActionResult<UpdateStartResponse>> StartUpdate(CancellationToken ct)
    {
        var result = await _update.StartUpdateAsync(ct);
        return result.Status switch
        {
            "started" => Accepted(result),
            "error" => StatusCode(500, result),
            _ => Ok(result)
        };
    }
}
