# Microsoft.Build (MSBuild)

The Microsoft Build Engine is a platform for building applications. This engine, also known as MSBuild, provides an XML schema for a project file that controls how the build platform processes and builds software. Visual Studio uses MSBuild, but MSBuild can run without Visual Studio. By invoking msbuild.exe on your project or solution file, you can orchestrate and build products in environments where Visual Studio isn't installed.

For more information on MSBuild, see the [MSBuild documentation](https://docs.microsoft.com/visualstudio/msbuild/msbuild) on docs.microsoft.com.

The [changelog](documentation/Changelog.md) has detailed information about changes made in different releases.

### Build Status

The current development branch is `main`. Changes in `main` will go into a future update of MSBuild, which will release with Visual Studio 17.5 and a corresponding version of the .NET Core SDK.

[![Build Status](https://dev.azure.com/dnceng/public/_apis/build/status/Microsoft/msbuild/msbuild-pr?branchName=main)](https://dev.azure.com/dnceng/public/_build/latest?definitionId=887&branchName=main)

We have forked for MSBuild 17.4 in the branch [`vs17.4`](https://github.com/Microsoft/msbuild/tree/vs17.4). Changes to that branch need special approval.

[![Build Status](https://dev.azure.com/dnceng/public/_apis/build/status/Microsoft/msbuild/msbuild-pr?branchName=vs17.4)](https://dev.azure.com/dnceng/public/_build/latest?definitionId=887&branchName=vs17.4)

17.2 builds from the branch [`vs17.2`](https://github.com/Microsoft/msbuild/tree/vs17.2). Only high-priority bugfixes will be considered for servicing 17.2.

[![Build Status](https://dev.azure.com/dnceng/public/_apis/build/status/Microsoft/msbuild/msbuild-pr?branchName=vs17.2)](https://dev.azure.com/dnceng/public/_build/latest?definitionId=887&branchName=vs17.2)

17.0 builds from the branch [`vs17.0`](https://github.com/Microsoft/msbuild/tree/vs17.0). Only high-priority bugfixes will be considered for servicing 17.0.

[![Build Status](https://dev.azure.com/dnceng/public/_apis/build/status/Microsoft/msbuild/msbuild-pr?branchName=vs17.0)](https://dev.azure.com/dnceng/public/_build/latest?definitionId=887&branchName=vs17.0)

16.11 builds from the branch [`vs16.11`](https://github.com/Microsoft/msbuild/tree/vs16.11). Only high-priority bugfixes will be considered for servicing 16.11.

[![Build Status](https://dev.azure.com/dnceng/public/_apis/build/status/Microsoft/msbuild/msbuild-pr?branchName=vs16.11)](https://dev.azure.com/dnceng/public/_build/latest?definitionId=887&branchName=vs16.11)

MSBuild 15.9 builds from the branch [`vs15.9`](https://github.com/dotnet/msbuild/tree/vs15.9). Only very-high-priority bugfixes will be considered for servicing 15.9.

## Building

### Building MSBuild with Visual Studio 2022 on Windows

For the full supported experience, you will need to have Visual Studio 2022 or higher.

To get started on **Visual Studio 2022**:

1. [Install Visual Studio 2022](https://www.visualstudio.com/vs/).  Select the following Workloads:
  - .NET desktop development
  - .NET Core cross-platform development
2. Ensure [long path support](https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation?tabs=registry#enable-long-paths-in-windows-10-version-1607-and-later) is enabled at the Windows level.
3. Open a `Developer Command Prompt for VS 2022` prompt.
4. Clone the source code: `git clone https://github.com/dotnet/msbuild`
  - You may have to [download Git](https://git-scm.com/downloads) first.
5. Run `.\build.cmd` from the root of the repo to build the code. This also restores packages needed to open the projects in Visual Studio.
6. Open `MSBuild.sln` or `MSBuild.Dev.slnf` in Visual Studio 2022.

Note: To create a usable MSBuild with your changes, run `.\build.cmd /p:CreateBootstrap=true`.
To build release, add `-c Release`: `.\build.cmd -c Release /p:CreateBootstrap=true`.

This newly-built MSBuild will be located at `artifacts\bin\bootstrap\net472\MSBuild\Current\Bin\MSBuild.exe`. It may not work for all scenarios, including C++ builds.

### Building MSBuild in Unix (Mac & Linux)

MSBuild can be run on Unix systems that support .NET Core. Set-up instructions can be viewed on the wiki: [Building Testing and Debugging on .Net Core MSBuild](documentation/wiki/Building-Testing-and-Debugging-on-.Net-Core-MSBuild.md)

## Localization

You can turn on localized builds via the `/p:LocalizedBuild=true` command line argument. For more information on localized builds and how to make contributions to MSBuild's translations, see our [localization documentation](documentation/wiki/Localization.md)

### Interested in contributing?
Before you contribute, please read through the contributing and developer guides to get an idea of what kinds of pull requests we accept.

* [Contributing Guide](documentation/wiki/Contributing-Code.md)
* **Developer Guide on:**
   - [.NET Core](documentation/wiki/Building-Testing-and-Debugging-on-.Net-Core-MSBuild.md)
   - [Full Framework](documentation/wiki/Building-Testing-and-Debugging-on-Full-Framework-MSBuild.md)
   - [Mono](documentation/wiki/Building-Testing-and-Debugging-on-Mono-MSBuild.md)

* See our [help wanted issues](https://github.com/dotnet/msbuild/issues?q=is%3Aopen+is%3Aissue+label%3A%22help+wanted%22) for a list of issues we think are great to onboard new developers.
   - **Note:** Please leave a comment asking to be assigned the issue if you want to work on it.
* See our [label documentation](documentation/wiki/Labels.md) for descriptions of labels we use throughout the repo.

### Other ways to contribute
We encourage any contributions you decide to make to the repo!

* [File an issue](https://github.com/dotnet/msbuild/issues/new/choose)
* [Start a discussion](https://github.com/dotnet/msbuild/discussions)

### MSBuild Components

* **MSBuild**. [Microsoft.Build.CommandLine](https://docs.microsoft.com/visualstudio/msbuild/msbuild)  is the entrypoint for the Microsoft Build Engine (MSBuild.exe).

* **Microsoft.Build**. The [Microsoft.Build](https://docs.microsoft.com/dotnet/api/?term=Microsoft.Build) namespaces contain types that provide programmatic access to, and control of, the MSBuild engine.

* **Microsoft.Build.Framework**. The [Microsoft.Build.Framework](https://docs.microsoft.com/dotnet/api/microsoft.build.framework) namespace contains the types that define how tasks and loggers interact with the MSBuild engine. For additional information on this component, see our [Microsoft.Build.Framework wiki page](documentation/wiki/Microsoft.Build.Framework.md).

* **Microsoft.Build.Tasks**. The [Microsoft.Build.Tasks](https://docs.microsoft.com/dotnet/api/microsoft.build.tasks) namespace contains the implementation of all tasks shipping with MSBuild.

* **Microsoft.Build.Utilities**. The [Microsoft.Build.Utilities](https://docs.microsoft.com/dotnet/api/microsoft.build.utilities) namespace provides helper classes that you can use to create your own MSBuild loggers and tasks.

### License

MSBuild is licensed under the [MIT license](LICENSE).
