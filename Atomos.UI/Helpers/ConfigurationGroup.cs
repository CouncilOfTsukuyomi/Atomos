using System.Collections.Generic;

namespace Atomos.UI.Helpers;

public class ConfigurationGroup
{
    public string GroupName { get; }
    public List<ConfigurationPropertyDescriptor> Properties { get; }

    public ConfigurationGroup(string groupName)
    {
        GroupName = groupName;
        Properties = new List<ConfigurationPropertyDescriptor>();
    }
}