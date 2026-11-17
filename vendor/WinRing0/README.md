# WinRing0

`WinRing0x64.dll` and `WinRing0x64.sys` are the OpenLibSys WinRing0 driver by hiyohiyo
(Crystal Dew World), taken unchanged from the RyzenAdj v0.19.0 Windows release
(https://github.com/FlyGoat/RyzenAdj/releases/tag/v0.19.0). WinRing0 is distributed under
the modified BSD license of the OpenLibSys project.

The driver has a published vulnerability (CVE-2020-14979) and Microsoft Defender blocks it
unless the folder is excluded. A PawnIO backend can replace it later by implementing
`ISmuRegisterAccess`.
