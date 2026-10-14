namespace btserver.Controllers.Dto;

public class UpdateProxyZoneConfigurationRequestDto
{
    public int DesiredInitialCount { get; set; } = 1;
    public string SelectionPolicyKey { get; set; } = "stable-id";
    public List<string> PreferredMachineIds { get; set; } = [];
}

public class ProxyZoneConfigurationDto : UpdateProxyZoneConfigurationRequestDto
{
    public Guid ZoneId { get; set; }
}

public class SetProxyDesignationsRequestDto
{
    public List<string> MachineIds { get; set; } = [];
}

public class ProxyStatusDto
{
    public Guid ZoneId { get; set; }
    public string ArtifactId { get; set; } = string.Empty;
    public int DesiredInitialCount { get; set; }
    public List<ProxyDesignationDto> Designations { get; set; } = [];
}

public class ProxyDesignationDto
{
    public string MachineId { get; set; } = string.Empty;
    public DateTime SelectedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ProxyTransitionDto
{
    public long Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public long Generation { get; set; }
}
