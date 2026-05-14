using btserver.Controllers.Dto;
using btserver.Controllers.Mapping;
using btserver.Swarm;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[SwaggerTag("Provides operations for querying machine information.")]
public class MachineController(MachineRegistry machineRegistry) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Retrieves a list of all currently active machines in the swarm.",
        Description = "Returns a list of all currently registered machines, including their loaded and pending artifacts."
    )]
    [SwaggerResponse(StatusCodes.Status200OK, "A list of machines.", typeof(List<MachineDto>))]
    public ActionResult<List<MachineDto>> GetMachines()
    {
        return Ok(machineRegistry.Machines.Values.Select(m => m.ToDto()).ToList());
    }
}