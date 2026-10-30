# GP Project Generator

A CA / TA project generator for Windows and Linux. Select features and enter parameters to generate the corresponding source code and build configuration.

- **CA**: A client application running in the normal operating system that sends requests to a TA.
- **TA**: A program running in a trusted execution environment that handles cryptographic operations, secure storage, and other tasks.

The Windows version provides a graphical interface that defaults to English and can be switched to Chinese. The Linux version uses a command-line interface.

## Features and GP Specification Scope

The generated code is based on the following GlobalPlatform documents:

1. **TEE Internal Core API Specification v1.3.1**: Internal TA interfaces.
2. **TEE Client API Specification v1.0**: CA client interfaces.
3. **TEE Client API v1.0 Errata and Precisions v2.0**: Corrections and clarifications for the client interfaces.

Peripheral and event APIs (Core Chapter 9), diagnostic examples, and deprecated compatibility APIs are excluded.

The following features can be generated:

- Digest, encryption/decryption, MAC, signing and verification, key agreement, and key derivation examples, including algorithms such as AES, RSA, SM2, SM3, and SM4.
- Property queries, secure storage, random numbers, time, memory, big integers, and session communication examples.
- Keys generated in the TA or supplied by the CA, with algorithm-specific settings such as IVs, messages, and authentication tag lengths.
- A CA configured as an executable, shared library, or static library, or a TA-only project.

The Windows utilities page also provides SM2 ZA and pre-signature digest calculation, certificate viewing and conversion, public key extraction, and fingerprint calculation.

Whether features can run on a device depends on the target TEE's implementation and configuration. Validation has passed in an OP-TEE 4.10.0 environment.

## Quick Start

### Windows x64

Download [GP-Project-Generator.exe](GP-Project-Generator-win-x64/GP-Project-Generator.exe) and double-click it to run. No additional environment setup is required. You can also download the entire repository and run the program in `GP-Project-Generator-win-x64/` directly.

1. **Define project**: Enter the name, UUID, and version, and select the CA output type.
2. **Choose features**: Add features, select algorithms, and enter parameters.
3. **Review & export**: Preview the code, choose a directory, and export the project.
4. **Build and run**: Follow the generated project's `README.md` to prepare the toolchains, build, and deploy the CA / TA.

For example, select AES-CBC encryption/decryption and SHA-256 digests, then choose an executable CA to generate a project containing both features. Running the compiled CA without arguments executes all selected features in sequence.

### Linux x64

Download [gp-project-generator](GP-Project-Generator-linux-x64/gp-project-generator). You can use the executable directly.

```bash
chmod +x gp-project-generator
./gp-project-generator --help
./gp-project-generator --example > project.json
# Edit project.json as needed, then validate and generate the project.
./gp-project-generator --validate project.json validate-report.json
./gp-project-generator --generate project.json generated generate-report.json
```

The main fields in `project.json` are:

- `Name`: Project name.
- `Uuid`: Unique identifier for the TA. Use a new UUID for a separate TA.
- `Version`: Version string, such as `"1.0.1"`.
- `CaOutput`: `executable`, `shared`, or `static` for an executable, shared library, or static library.
- `Features`: Features, algorithms, and parameters to generate. All values in `Options` are strings.

## Generate Projects with AI

Give [AI_GUIDE.md](AI_GUIDE.md) and the program path to an AI assistant that can read local files and execute commands, then describe your requirements. The AI prepares a JSON configuration, validates it, and invokes the generator. Windows GUI users do not need to write JSON manually.

Example prompts:

> Read AI_GUIDE.md and use GP-Project-Generator.exe in the current directory to generate an AES-CBC encryption/decryption and SHA-256 digest project. Supply the key and IV from the CA and build the CA as a shared library. Validate the configuration first, then export to generated/demo.

> Read AI_GUIDE.md and use ./gp-project-generator in the current directory to generate an SM2 signing and verification project. Generate keys in the TA, include the ZA calculation function, and build the CA as an executable. Tell me where the files are, how to build them, and which checks have been completed.

Both versions support the following three commands. On Linux, replace the program name with `./gp-project-generator`:

```text
GP-Project-Generator.exe --describe catalog.json
GP-Project-Generator.exe --validate project.json validate-report.json
GP-Project-Generator.exe --generate project.json generated generate-report.json
```

When invoking the Windows program from PowerShell, wait for the process to exit before reading its report. See the AI guide for complete examples.

## Build the Generator from Source

### Windows

Install the **.NET 10 SDK**, then run the following in the repository directory:

```powershell
.\build.ps1 -SingleFile
```

The program is generated at `release-single/GP-Project-Generator.exe`. If the SDK is not on PATH, specify its location:

```powershell
.\build.ps1 -SingleFile -DotnetPath 'C:\tools\dotnet\dotnet.exe'
```

The SDK is only needed to build the generator, not to run the single-file EXE. When distributing the program, retain the licenses and third-party notices from the build output.

### Linux x64

Install the **.NET 10 SDK and Python 3**, then run the following in the repository directory:

```bash
bash build-linux.sh
# To select a local SDK:
DOTNET_PATH=/path/to/dotnet bash build-linux.sh
```

```text
release-linux-x64/gp-project-generator
release-linux-x64/AI_GUIDE.md
release-linux-x64.tar.gz
```

The directory and archive contain the single-file program `gp-project-generator` and a separate `AI_GUIDE.md`. Only the executable is needed to run the program; when using AI assistance, provide the guide and program path together. The Windows distribution likewise includes an EXE and a separate AI guide.

The first build downloads clang-format 18.1.8 from PyPI and verifies its published SHA-256. Build tools are stored in ignored `.tools/`; do not commit the SDK. The build checks that the publish directory contains only the program and guide, then copies and renames the executable alone for command-line checks. It reports success only after those checks pass.

Use `bash build-linux.sh --output custom-directory` to choose an output location. The directory must be empty or contain only the previous `gp-project-generator` and `AI_GUIDE.md` (either may be absent). If an old directory contains other files, choose a different one; the script does not delete unrelated existing files.

## License

The project's original source code, templates, and icon use the [BSD Zero Clause License (0BSD)](LICENSE): use, commercial use, modification, and distribution are permitted, including use in closed-source projects, without requiring attribution for the original portions.

Third-party components remain subject to their respective licenses. See [NOTICE.md](NOTICE.md). The standard license text is available from the [Open Source Initiative](https://opensource.org/license/0bsd).
