# Third-party notices

Original project code, templates and artwork use [0BSD](LICENSE).
Third-party material keeps the licenses below.

The application source, C templates and geometric icon in this repository are
independent implementations. Public API identifiers and declaration metadata in
`assets/catalog.json` and `assets/attributes.json` were checked against these
upstream OP-TEE 4.10.0 headers:

- https://github.com/OP-TEE/optee_os/tree/4.10.0/lib/libutee/include
- https://github.com/OP-TEE/optee_client/tree/4.10.0/libteec/include

The relevant header notices are retained below. Complete upstream source trees
are not included. GlobalPlatform specifications remain subject to their own
terms and are not redistributed. The project is not an official GlobalPlatform
or OP-TEE product.

## OP-TEE header metadata — BSD 2-Clause

Copyright (c) 2014, STMicroelectronics International N.V.
Copyright (c) 2015, 2022, Linaro Limited
Copyright (c) 2020, 2022-2023 Linaro Limited
Copyright (c) 2022, Technology Innovation Institute (TII)
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.

## .NET binary releases

Self-contained releases also contain Microsoft .NET runtime components. The
build script includes the installed .NET SDK's LICENSE and ThirdPartyNotices
when available. The bundled .NET notices are under `licenses/dotnet/`. Source users obtain .NET separately.

## LLVM clang-format

Generated C/H files use LLVM clang-format 18.1.8 for Windows x64, from
https://pypi.org/project/clang-format/18.1.8/
Wheel: clang_format-18.1.8-py2.py3-none-win_amd64.whl
Wheel SHA-256: 6ca6768270e291495174faa6d8c082aa0181722b29ef8e36558b397069fa4a2d
The checksum was verified against PyPI before extracting the executable.
See tools/formatter/LICENSE.md and COPYING.md for upstream licenses.
The single-file release embeds the formatter and its notices.

## Algorithm references

The desktop SM3 and SM2 helpers follow public algorithm descriptions:
https://datatracker.ietf.org/doc/html/draft-sca-cfrg-sm3-02
https://datatracker.ietf.org/doc/html/draft-shen-sm2-ecdsa-02
These are Internet-Drafts, not published RFC standards.
HKDF sample inputs follow RFC 5869 Appendix A.1:
https://www.rfc-editor.org/rfc/rfc5869#appendix-A.1
