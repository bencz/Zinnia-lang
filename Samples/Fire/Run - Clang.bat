@echo Compiling: Fire.cpp
@clang++ Fire.cpp -O3 -c -o Fire.o
@..\..\Binaries\Zinnia.exe -x Fire.o ..\CppMain.zinnia