﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.DotNet.MSBuildSdkResolver
{
    public sealed class DotNetMSBuildSdkResolver : SdkResolver
    {
        public override string Name => "Microsoft.DotNet.MSBuildSdkResolver";

        // Default resolver has priority 10000 and we want to go before it and leave room on either side of us. 
        public override int Priority => 5000;

        private readonly Func<string, string> _getEnvironmentVariable;

        public DotNetMSBuildSdkResolver() 
            : this(Environment.GetEnvironmentVariable)
        {
        }

        // Test hook to provide environment variables without polluting the test process.
        internal DotNetMSBuildSdkResolver(Func<string, string> getEnvironmentVariable)
        {
            _getEnvironmentVariable = getEnvironmentVariable;
        }

        public override SdkResult Resolve(SdkReference sdkReference, SdkResolverContext context, SdkResultFactory factory)
        {
            // These are overrides that are used to force the resolved SDK tasks and targets to come from a given
            // base directory and report a given version to msbuild (which may be null if unknown. One key use case
            // for this is to test SDK tasks and targets without deploying them inside the .NET Core SDK.
            string msbuildSdksDir = _getEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR");
            string netcoreSdkVersion = _getEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER");

            if (msbuildSdksDir == null)
            {
                string netcoreSdkDir = ResolveNetcoreSdkDirectory(context);
                if (netcoreSdkDir == null)
                {
                    return factory.IndicateFailure(
                        new[]
                        {
                            "Unable to locate the .NET Core SDK. Check that it is installed and that the version"
                            + " specified in global.json (if any) matches the installed version."
                        });
                }

                msbuildSdksDir = Path.Combine(netcoreSdkDir, "Sdks");
                netcoreSdkVersion = new DirectoryInfo(netcoreSdkDir).Name;;
            }

            if (!IsNetCoreSDKOveridden(netcoreSdkVersion) && 
                IsNetCoreSDKSmallerThanTheMinimumVersion(netcoreSdkVersion, sdkReference.MinimumVersion))
            {
                return factory.IndicateFailure(
                    new[]
                    {
                        $"Version {netcoreSdkVersion} of the SDK is smaller than the minimum version"
                        + $" {sdkReference.MinimumVersion} requested. Check that a recent enough .NET Core SDK is"
                        + " installed, increase the minimum version specified in the project, or increase"
                        + " the version specified in global.json."
                    });
            }

            string msbuildSdkDir = Path.Combine(msbuildSdksDir, sdkReference.Name, "Sdk");
            if (!Directory.Exists(msbuildSdkDir))
            {
                return factory.IndicateFailure(
                    new[] 
                    {
                        $"{msbuildSdkDir} not found. Check that a recent enough .NET Core SDK is installed"
                        + " and/or increase the version specified in global.json."
                    });
            }

            return factory.IndicateSuccess(msbuildSdkDir, netcoreSdkVersion);
        }

        private bool IsNetCoreSDKOveridden(string netcoreSdkVersion)
        {
            return netcoreSdkVersion == null;
        }

        private bool IsNetCoreSDKSmallerThanTheMinimumVersion(string netcoreSdkVersion, string minimumVersion)
        {
            FXVersion netCoreSdkFXVersion;
            FXVersion minimumFXVersion;

            if (string.IsNullOrEmpty(minimumVersion))
            {
                return false;
            }

            if (!FXVersion.TryParse(netcoreSdkVersion, out netCoreSdkFXVersion) ||
                !FXVersion.TryParse(minimumVersion, out minimumFXVersion))
            {
                return true;
            }

            return FXVersion.Compare(netCoreSdkFXVersion, minimumFXVersion) == -1;
        }

        private string ResolveNetcoreSdkDirectory(SdkResolverContext context)
        {
            string exeDir = GetDotnetExeDirectory();
            string workingDir = context.SolutionFilePath ?? context.ProjectFilePath;
            string netcoreSdkDir = Interop.hostfxr_resolve_sdk(exeDir, workingDir);

            return netcoreSdkDir;
        }

        private string GetDotnetExeDirectory()
        {
            string environmentOverride = _getEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
            if (environmentOverride != null)
            {
                return environmentOverride;
            }

            var environmentProvider = new EnvironmentProvider(_getEnvironmentVariable);

            return Path.GetDirectoryName(environmentProvider.GetCommandPath("dotnet"));
        }
    }
}
