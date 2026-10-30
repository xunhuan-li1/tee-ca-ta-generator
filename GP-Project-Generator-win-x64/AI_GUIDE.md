# GP Project Generator — AI Guide

Use this guide to turn a user request into a CA/TA project. You need local file access and permission to run `GP-Project-Generator.exe` on Windows or `./gp-project-generator` on Linux. The published packages run without Python or a .NET SDK. Compiling the generated project requires a separate OP-TEE development environment.

The UI starts in English. Users can switch to Chinese from the language dropdown for the current session.

## Linux command-line usage

The Linux x64 package uses the same configuration and commands described below. Replace `GP-Project-Generator.exe` with `./gp-project-generator`. There is no graphical interface on Linux. Only the executable is required to run. The formatter and runtime are embedded. The guide is supplied as a separate `AI_GUIDE.md` in both the Windows and Linux distributions; it is not embedded in the program. An AI assistant can read it directly without an export command. Run `./gp-project-generator --help` for usage or `--example > project.json` for an initial configuration. Use `--licenses` to read the Linux program's embedded licenses and third-party notices. Components are extracted to a writable cache that must allow execution; the program directory does not need to be writable.

```bash
./gp-project-generator --describe catalog.json
./gp-project-generator --validate project.json validate-report.json
./gp-project-generator --generate project.json generated generate-report.json
```

The shell waits for each command automatically. Exit code 0 means success; 1 means an error. Errors are printed to standard error and, once validation or generation starts, recorded in the JSON report. Early usage or path errors may not produce a new report. Check the exit code and the current report's `Success` field before continuing. A successful generation report does not mean the CA/TA has been compiled or executed. Start with the configuration exported by `--example`; change its UUID for a separate TA and select a new output directory.

## Start here

1. Confirm the executable path and working directory. Run `--describe catalog.json` to discover the available features, algorithms, curves, property presets, defaults, and login methods.
2. Convert the user's request into a UTF-8 JSON configuration. Include only relevant settings. Generate a new UUID for a new TA; preserve a UUID supplied by the user or belonging to an existing TA. Do not invent options absent from the catalog.
3. Run `--validate project.json validate-report.json`. Wait for completion and check both the exit code and the report's `Success` field. Correct configuration errors before proceeding.
4. Run `--generate project.json generated generate-report.json`. The output directory must be new or empty. Store the report outside the output directory and separately from the input configuration.
5. Report the configuration path, generated source path, and checks actually performed. Successful generation does not establish compilation or runtime success.

The executable accepts structured configuration, not natural-language requests. The AI converts requests into configuration and invokes the commands. GUI users can configure and export projects without writing JSON.

## Commands and reports

```text
GP-Project-Generator.exe --describe catalog.json
GP-Project-Generator.exe --validate project.json validate-report.json
GP-Project-Generator.exe --generate project.json generated generate-report.json
```

This is a GUI-subsystem executable. In PowerShell, explicitly wait for each process before reading its report:

```powershell
$exe = (Resolve-Path '.\GP-Project-Generator.exe').Path
$p = Start-Process -FilePath $exe -ArgumentList '--describe catalog.json' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Describe failed' }

$p = Start-Process -FilePath $exe -ArgumentList '--validate project.json validate-report.json' -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content validate-report.json; throw 'Validation failed' }

$p = Start-Process -FilePath $exe -ArgumentList '--generate project.json generated generate-report.json' -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content generate-report.json; throw 'Generation failed' }
```

Quote argument paths containing spaces, for example `-ArgumentList '--describe "C:\work folder\catalog.json"'`. Exit code `0` indicates success. Failures during a valid command are written to its report. Early errors, such as invalid argument counts or conflicting paths, are logged to `%TEMP%\gp-project-generator-error.txt`. An existing report file alone does not prove success; check the exit code and current report contents.

For digests, `Features[].Options.streaming` is the string `"false"` for single-pass processing or `"true"` for streaming. The independent `copyReuse` option accepts the strings `"true"` and `"false"`, defaults to `"false"`, and works with either processing mode. Set `copyReuse` to `"true"` to include operation-state copying, reset/reuse, and digest comparison examples.

## Configuration example

This example generates AES-CBC encryption/decryption and a SHA-256 digest, with the CA built as a shared library. Replace the example UUID with a new UUID for a new project.

```json
{
  "FormatVersion": 1,
  "Version": "1.0.1",
  "Name": "gp_demo",
  "Uuid": "c5bbdba8-6e70-457e-b26a-b34bcc1d028c",
  "GenerateCa": true,
  "CaOutput": "shared",
  "SessionContext": false,
  "Features": [
    {
      "Kind": "cipher",
      "Algorithm": "TEE_ALG_AES_CBC_NOPAD",
      "Options": {
        "keySource": "input",
        "key": "000102030405060708090a0b0c0d0e0f",
        "ivSource": "input",
        "iv": "000102030405060708090a0b0c0d0e0f",
        "message": "00112233445566778899aabbccddeeff",
        "streaming": "false"
      }
    },
    {
      "Kind": "digest",
      "Algorithm": "TEE_ALG_SHA256",
      "Options": { "message": "616263" }
    }
  ]
}
```

- Required fields: `FormatVersion`, `Name`, `Uuid`, and `Features`. Names are case-sensitive; duplicate or unknown fields are rejected.
- `Name` starts with a lowercase letter, contains only lowercase letters, digits, and underscores, and has at most 48 characters. The UUID must be a standard, nonzero UUID.
- `CaOutput` selects `executable`, `shared`, or `static`. Set `GenerateCa` to `false` to export only the TA.
- Each `Features` entry represents one capability/command. Repeat a `Kind` to select multiple algorithms. A cryptographic `Algorithm` must belong to the family matching its `Kind`.
- All `Options` values are strings, including numbers and booleans. Project-level booleans use JSON `true` or `false`.
- `SessionContext`, `SingleInstance`, `MultiSession`, and `KeepAlive` are optional booleans. `KeepAlive` requires `SingleInstance`. `StackKiB` defaults to 16; `HeapKiB` defaults to 256.
- `Description` has at most 512 characters. `Version` is a nonempty string without control characters, defaults to `"1.0.0"`, and can be `"1.0.1"` or `"1.0.1-rc.1"`.
- `Login` defaults to `TEEC_LOGIN_PUBLIC`. The six GP client login methods listed in the catalog are supported as configuration choices. Group login uses the project-level numeric `GroupId`; the platform determines the actual identity.

## Mapping user requirements

| Requirement | Configuration |
|---|---|
| Digest | `Kind=digest`, for example `TEE_ALG_SHA256`; SHAKE supports `outputBytes` |
| CBC / CTR / XTS | `Kind=cipher`; configure the algorithm and IV where applicable; XTS requires two distinct keys and a tweak |
| GCM / CCM | `Kind=ae`, with `iv`, `aad`, and `tagBits` |
| MAC | `Kind=mac`, selecting HMAC, CMAC, or CBC-MAC |
| RSA / SM2 encryption | `Kind=encrypt`; imported RSA keys use `rsaN/rsaE/rsaD`; SM2 uses `private/publicX/publicY` |
| Signing and verification | `Kind=sign`; SM2 `zaSource=compute` includes the ZA function; `input` requires a 32-byte `za` matching the imported key and signer identity |
| Key agreement / HKDF | `Kind=derive`; imported agreement keys also require the peer public key; HKDF uses SHA-256 with configurable IKM, salt, info, and output length |
| Properties | `Kind=property`; select `set/name/type` from `PropertyPresets`, or specify an existing custom property |
| Secure storage | `Kind=storage`, selecting `action` and object ID; the default roundtrip creates, verifies reads/writes, then deletes the object |
| TA-to-TA calls | `Kind=session`; supply a deployed target's `targetUuid` and `command`, then adapt parameters to its protocol |
| CA shared memory / cancellation | `shared_memory` / `client_cancel` |

Read `UseCases` from `--describe` for task-oriented entries. Optional `Feature.Scenario` selects a use-case ID and must match `Kind` and `Algorithm`; configurations that omit it remain valid. `Scenarios` defines the complete feature and parameter catalog. Its `Fields` include shared cryptographic options, so not every field applies to every algorithm. Set relevant options and leave the rest at their algorithm-dependent defaults. Hexadecimal inputs represent bytes and may contain spaces. CBC/NOPAD messages must be block-aligned.

`keySource=generate` generates keys inside the TA. With `input`, an executable project supplies inputs in `ca/examples/` and passes them through named interfaces to the TA; these key values are not embedded in TA code. Library outputs omit default inputs: callers supply messages and keys using the input structures in `ca/client_api.h`. Each byte field uses a pointer and length; lengths must satisfy the selected algorithm, key size, and protocol capacity. Built-in example keys and the keys above are public data. Do not describe them as secret deployment keys.

## Generated project and build

Typical output includes `common/gp_protocol.h`, `ta/Makefile`, `ta/sub.mk`, TA entry and feature sources, `ta/user_ta_header_defines.h`, `ca/Makefile`, `ca/Android.mk`, CA interfaces, and an optional `main`.

```sh
make -C ta TA_DEV_KIT_DIR=/path/to/export-ta_arm64 CROSS_COMPILE=/path/to/aarch64-linux-gnu-
make -C ca TEEC_EXPORT=/path/to/staging/usr CROSS_COMPILE=/path/to/aarch64-linux-gnu-
```

The TA output is a UUID-named `.ta` file, signed using the target development kit's configuration. A static CA library does not merge `libteec`; a shared CA library depends on a separate `libteec.so`. The generated `Android.mk` requires the target environment to provide the `libteec` module and `TEEC_INCLUDE_DIR`. The generator does not include development kits, cross-compilers, or device signing keys.

An executable CA runs all selected commands by default, using a separate session for each command. Use `--help` or `--list` to inspect commands, `--all` to run all commands, or a single command name to run one. Storage operations modify the specified objects. Confirm target support for algorithms, curves, optional storage backends, and login methods.

The GUI export location is a parent directory; duplicate project names receive suffixes such as `_2`. The CLI output argument is the final project directory, and a nonempty directory is rejected. When CA generation is disabled, shared-memory and cancellation scenarios retain their configuration but are omitted from generation.

## Scope and additional parameters

The generator targets GP Internal Core API 1.3.1, Client API 1.0, and Client API Errata and Precisions 2.0. It excludes diagnostics (`TEE_Panic`), deprecated compatibility APIs, compatibility algorithm aliases, and all of Core chapter 9, Peripheral and Event APIs. Use only the options returned by `--describe`.

- RSA OAEP `mgfHash` accepts `same` (the OAEP digest) or a listed digest; `label` is hexadecimal.
- Imported RSA keys with `rsaCrt=true` require `rsaP/rsaQ/rsaDp/rsaDq/rsaQinv`, as well as n/e/d.
- Property enumeration supports `set=current_ta/current_client/implementation`.
- Use `--describe` for the full feature and API catalog. Coverage means examples are available; not every parameter combination or business flow is generated.

Do not add company-specific interfaces or present OP-TEE extensions outside this GP catalog as standard GP interfaces. Distinguish generation, compilation, target execution, and compliance certification. Preserve the report's `Compiled=false` value accurately.

## Target runtime differences

Without an explicit ECDSA curve, SHA1/SHA224/SHA256/SHA384/SHA512 use P192/P224/P256/P384/P521 respectively. An explicitly selected curve is preserved. A target may restrict digest/curve combinations.

The existing OP-TEE 4.10.0 QEMU environment includes earlier cryptographic backend fixes. The latest 312 runtime cases produced 230 successes, 82 unsupported results, and no unexpected failures. These results apply to the tested configurations and do not describe an unmodified upstream release. A selectable option, successful compilation, or standalone capability query does not establish successful execution. Check each command's result code and output.

Generated code follows the three GP documents above. OP-TEE is the build and validation environment, not the limit of the standard feature set. Do not substitute vendor algorithms or remove standard GP choices solely because a particular OP-TEE version lacks support. Porting to another TEE requires adapting build integration, property packaging, and signing.

## Example user request

> Generate AES-CBC encryption/decryption and SHA-256 digest code. Pass the key
> and IV from the CA, build the CA as a shared library, and export to
> generated/demo. Validate the configuration first.

Report where the files were written and whether you generated, compiled, or
ran them. Link to the generated README for the target build commands.

Known scope limits: no configurable DH private-key bit count, no SM2 key
exchange confirmation flow, no persistent-key workflow, and no separate
public-key-only encryption or external-signature verification workflow.
OpenSession uses no operation parameters; adapt TA-to-TA calls to the target
protocol. Example capacities are 2048 message bytes, 512 bytes per attribute
and 4096 returned bytes; these are project limits, not GP limits.

The tested OP-TEE 4.10.0 backend does not support every offered option. Examples
include Ed448/X448, GP HKDF, SM4 PKCS5 modes, Brainpool curves and several
SHA-3 signature combinations. Optional storage and login modes depend on the
target. Earlier tests also encountered OAEP SHA-3 MGF1 parameter errors,
SM2 key exchange faults, HMAC-SM3 long-key handling faults, and some SM2
verification failures. Binary version property queries and empty-plaintext
SM2 encryption failed in that setup. Do not assume these issues occur in every
OP-TEE version or equate a fixed test environment with an unmodified upstream
release. The generator does not fix or update the target backend; check the
target implementation before using these features.

For key size, omit `keyBits` to use the selected algorithm's default or supply a valid positive bit count. The GUI displays the actual default size. Old configurations using the `"0"` default marker are normalized when loaded.
