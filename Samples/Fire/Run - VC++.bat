@echo Compiling: Fire.cpp
@call "C:\Program Files (x86)\Microsoft Visual Studio 11.0\VC\vcvarsall.bat" x86
@cl.exe Fire.cpp /c -FoFire.o -Ox
@..\..\Binaries\Zinnia.exe ..\msvcrt.lib ..\CppMain.zinnia -x Fire.o