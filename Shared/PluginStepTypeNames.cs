using System.Globalization;
using System.Text;

namespace XrmPluginCore.Internal;

// Compiled into both the generator and the runtime so naming and discovery cannot drift.
internal static class PluginStepTypeNames
{
	// Custom APIs use the ActionWrapper suffix in the same namespace. Keep step actions distinct.
	public const string ActionSuffix = "StepAction";

	public static string Prefix(string plugin, string entity, string operation, string stage) =>
		$"{Encode(plugin)}_{Encode(entity)}_{Encode(operation)}_{Encode(stage)}";

	private static string Encode(string component)
	{
		var result = new StringBuilder(component?.Length ?? 0);
		foreach (var character in component ?? string.Empty)
		{
			// '_' is reserved for boundaries and 'Z' introduces exactly four hex digits.
			// Encoding (rather than replacing) keeps distinct custom messages distinct.
			if (character is >= 'A' and <= 'Y' or >= 'a' and <= 'z' or >= '0' and <= '9')
				result.Append(character);
			else
				result.Append('Z').Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
		}
		return result.ToString();
	}
}
