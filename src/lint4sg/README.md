# lint4sg

[![NuGet Version](https://img.shields.io/nuget/v/lint4sg?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/lint4sg/)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/lint4sg/test.yaml?branch=main&label=Test&style=flat-square)

A strict Roslyn analyzer that enforces best practices for **.NET Source Generator** projects.

Source Generators have strict performance and correctness requirements that differ significantly from ordinary C# code. This analyzer catches common mistakes at compile time, acting as a safety net especially when AI coding assistants generate Source Generator code.

See: [GitHub Repository](https://github.com/arika0093/lint4sg)