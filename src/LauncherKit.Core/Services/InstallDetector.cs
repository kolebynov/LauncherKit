﻿using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class InstallDetector : IInstallDetector
{
	private const string VcRedistInstallInfoKey =
		@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes";

	private readonly IRegistryManager _registryManager;

	public InstallDetector(IRegistryManager registryManager)
	{
		_registryManager = registryManager;
	}

	public bool IsVcRedistInstalled(VcRedistSupportedArchitecture architecture)
	{
		var installInfoKeyName = @$"{VcRedistInstallInfoKey}\{architecture.Name}";
		return _registryManager.GetValue(installInfoKeyName, "Installed") is 1 &&
			   _registryManager.GetValue(installInfoKeyName, "Major") is 14 &&
			   _registryManager.GetValue(installInfoKeyName, "Minor") is >= 44;
	}

	public union VcRedistSupportedArchitecture(VcRedistSupportedArchitecture.X86, VcRedistSupportedArchitecture.X64)
	{
		public sealed record X86;

		public sealed record X64;

		/// <summary>The name the VC++ redistributable uses for the architecture in its registry key and installer.</summary>
		public string Name =>
			this switch
			{
				X86 => nameof(X86),
				X64 => nameof(X64),
			};
	}
}