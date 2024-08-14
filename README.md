# cdc

> **c**# **d**ecompiler & **c**ustomizer; c# modding/patching library designed for making modifications to decompiled source code

---

***cdc*** is a library for modifying in-production .NET applications by decompiling IL to raw C# source code (through `ICSharpCode.Decompiler` [ILSpy]) and publishing patches by diffing your modifications against the original source code. It is designed such that any consumers can reproducibly obtain an identical copy of the decompiled source code so that patches may be applied consistently.
