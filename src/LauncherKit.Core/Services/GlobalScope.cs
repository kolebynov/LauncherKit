﻿using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Core.Db;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class GlobalScope : IGlobalScope
{
	private readonly IServiceScope _scope;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private GlobalDbContext? _globalDbContext;

	public GlobalScope(IServiceProvider serviceProvider)
	{
		_scope = serviceProvider.CreateScope();
	}

	public EnteredScope EnterScope()
	{
		_lock.Wait();
		return new EnteredScope(this);
	}

	public void Dispose()
	{
		_lock.Dispose();
		_scope.Dispose();
	}

	public readonly struct EnteredScope : IDisposable
	{
		private readonly GlobalScope _globalScope;

		public IServiceProvider ServiceProvider => _globalScope._scope.ServiceProvider;

		public GlobalDbContext GlobalDbContext => _globalScope._globalDbContext ??= ServiceProvider.GetRequiredService<GlobalDbContext>();

		public EnteredScope(GlobalScope globalScope)
		{
			_globalScope = globalScope;
		}

		public void Dispose()
		{
			_globalScope._lock.Release();
		}
	}
}