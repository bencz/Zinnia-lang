	format MS COFF
	extrn _bb_BeginPerf
	extrn _bb_EndPerf
	extrn _bbGCCollect
	extrn _bbGCSuspend
	extrn _bbHandleFromObject
	extrn _bbMilliSecs
	extrn _brl_d3d9max2d_D3D9Max2DDriver
	extrn _brl_graphics_SetGraphicsDriver
	extrn _brl_graphics_Graphics
	extrn _brl_graphics_Flip
	extrn _brl_polledinput_AppTerminate
	extrn _brl_polledinput_KeyHit
	extrn _brl_polledinput_MouseX
	extrn _brl_polledinput_MouseY
	extrn _brl_max2d_CreateImage
	extrn _brl_max2d_LockImage
	extrn _brl_max2d_UnlockImage
	extrn _brl_max2d_ImageWidth
	extrn _brl_max2d_ImageHeight
	extrn _brl_max2d_Cls
	extrn _bb_DrawFrameStats
	extrn _brl_max2d_DrawRect
	extrn _brl_max2d_SetColor
	extrn _brl_max2d_SetBlend
	extrn _brl_max2d_SetAlpha
	extrn _brl_max2d_DrawImage
	extrn _brl_max2d_DrawImageRect
	extrn _brl_max2d_DrawPixmap
	extrn _brl_pixmap_CreatePixmap
	extrn _brl_pixmap_CopyPixmap
	extrn _brl_pixmap_ConvertPixmap
	extrn _brl_pixmap_PixmapWidth
	extrn _brl_pixmap_PixmapHeight
	extrn _brl_pixmap_PixmapPitch
	extrn _brl_pixmap_PixmapFormat
	extrn _brl_pixmap_PixmapPixelPtr
	extrn _brl_pixmap_PixmapWindow
	extrn _brl_pixmap_ResizePixmap
	extrn _brl_pixmap_ClearPixels
	extrn _brl_pixmap_WritePixel
	extrn _brl_pixmap_ReadPixel
	public _BlitzMax_Graphics%1
	public _BlitzMax_CreateImage%1
	public _BlitzMax_GetMousePosition
	public _BlitzMax_DrawImage%1
	public _BlitzMax_DrawImageRect%1
	extrn _bbStartup
	extrn ___bb_appstub_appstub
	extrn _bbGCStackTop
	extrn _System_Object_ToString
	extrn _System_Memory_Zero%1
	extrn _System_Environment_CommandLineArguments2_get
	extrn _System_ValueType_ToString
	extrn _%ZinniaCore
	public _%BlitzMax
	
section ".text" code readable executable

_BlitzMax_Main:
	push ebx
	push ebp
	mov ebx, esp
	and esp, 4294967288
	mov ebp, esp
	sub esp, 80
	movdqu dqword[ebp - 64], xmm4
	movdqu dqword[ebp - 48], xmm5
	movdqu dqword[ebp - 32], xmm6
	movdqu dqword[ebp - 16], xmm7
	lea eax, [ebp - 80]
	call _System_Environment_CommandLineArguments2_get
	sub esp, 16
	mov eax, dword[ebp - 76]
	mov dword[esp], eax
	mov eax, dword[ebp - 80]
	mov dword[esp + 4], eax
	mov dword[esp + 8], 0
	mov dword[esp + 12], 0
	call _bbStartup
	add esp, 16
	add dword[_bbGCStackTop], 72
	call ___bb_appstub_appstub
	sub esp, 8
	call _brl_d3d9max2d_D3D9Max2DDriver
	mov dword[esp], eax
	mov dword[esp + 4], 2
	call _brl_graphics_SetGraphicsDriver
	add esp, 8
	movdqu xmm4, dqword[ebp - 64]
	movdqu xmm5, dqword[ebp - 48]
	movdqu xmm6, dqword[ebp - 32]
	movdqu xmm7, dqword[ebp - 16]
	mov esp, ebx
	pop ebp
	pop ebx
	ret 

_BlitzMax_Graphics%1:
	push ebx
	sub esp, 64
	movdqu dqword[esp], xmm4
	movdqu dqword[esp + 16], xmm5
	movdqu dqword[esp + 32], xmm6
	movdqu dqword[esp + 48], xmm7
	sub esp, 20
	mov ebx, dword[esp + 92]
	mov dword[esp], ebx
	mov ebx, dword[esp + 96]
	mov dword[esp + 4], ebx
	mov dword[esp + 8], eax
	mov dword[esp + 12], edx
	mov dword[esp + 16], ecx
	call _brl_graphics_Graphics
	add esp, 20
	movdqu xmm4, dqword[esp]
	movdqu xmm5, dqword[esp + 16]
	movdqu xmm6, dqword[esp + 32]
	movdqu xmm7, dqword[esp + 48]
	add esp, 64
	pop ebx
	ret 8

_BlitzMax_CreateImage%1:
	sub esp, 64
	movdqu dqword[esp], xmm4
	movdqu dqword[esp + 16], xmm5
	movdqu dqword[esp + 32], xmm6
	movdqu dqword[esp + 48], xmm7
	sub esp, 16
	mov ecx, dword[esp + 84]
	mov dword[esp], ecx
	mov ecx, dword[esp + 88]
	mov dword[esp + 4], ecx
	mov dword[esp + 8], eax
	mov dword[esp + 12], edx
	call _brl_max2d_CreateImage
	add esp, 16
	movdqu xmm4, dqword[esp]
	movdqu xmm5, dqword[esp + 16]
	movdqu xmm6, dqword[esp + 32]
	movdqu xmm7, dqword[esp + 48]
	add esp, 64
	ret 8

_BlitzMax_GetMousePosition:
	push ebx
	sub esp, 64
	movdqu dqword[esp], xmm4
	movdqu dqword[esp + 16], xmm5
	movdqu dqword[esp + 32], xmm6
	movdqu dqword[esp + 48], xmm7
	mov ebx, eax
	call _brl_polledinput_MouseX
	mov dword[ebx], eax
	call _brl_polledinput_MouseY
	mov dword[ebx + 4], eax
	movdqu xmm4, dqword[esp]
	movdqu xmm5, dqword[esp + 16]
	movdqu xmm6, dqword[esp + 32]
	movdqu xmm7, dqword[esp + 48]
	add esp, 64
	pop ebx
	ret 

_BlitzMax_ImageSize:
	push ebx
	push ebp
	sub esp, 64
	movdqu dqword[esp], xmm4
	movdqu dqword[esp + 16], xmm5
	movdqu dqword[esp + 32], xmm6
	movdqu dqword[esp + 48], xmm7
	mov ebx, eax
	mov ebp, edx
	sub esp, 4
	mov dword[esp], ebp
	call _brl_max2d_ImageWidth
	add esp, 4
	mov dword[ebx], eax
	sub esp, 4
	mov dword[esp], ebp
	call _brl_max2d_ImageHeight
	add esp, 4
	mov dword[ebx + 4], eax
	movdqu xmm4, dqword[esp]
	movdqu xmm5, dqword[esp + 16]
	movdqu xmm6, dqword[esp + 32]
	movdqu xmm7, dqword[esp + 48]
	add esp, 64
	pop ebp
	pop ebx
	ret 

_BlitzMax_DrawImage%1:
	sub esp, 64
	movdqu dqword[esp], xmm4
	movdqu dqword[esp + 16], xmm5
	movdqu dqword[esp + 32], xmm6
	movdqu dqword[esp + 48], xmm7
	sub esp, 16
	mov dword[esp], eax
	movss xmm0, dword[esp + 84]
	movss dword[esp + 4], xmm0
	movss xmm0, dword[esp + 88]
	movss dword[esp + 8], xmm0
	mov dword[esp + 12], edx
	call _brl_max2d_DrawImage
	add esp, 16
	movdqu xmm4, dqword[esp]
	movdqu xmm5, dqword[esp + 16]
	movdqu xmm6, dqword[esp + 32]
	movdqu xmm7, dqword[esp + 48]
	add esp, 64
	ret 8

_BlitzMax_DrawImageRect%1:
	sub esp, 64
	movdqu dqword[esp], xmm4
	movdqu dqword[esp + 16], xmm5
	movdqu dqword[esp + 32], xmm6
	movdqu dqword[esp + 48], xmm7
	sub esp, 24
	mov dword[esp], eax
	movss xmm0, dword[esp + 92]
	movss dword[esp + 4], xmm0
	movss xmm0, dword[esp + 96]
	movss dword[esp + 8], xmm0
	movss xmm0, dword[esp + 100]
	movss dword[esp + 12], xmm0
	movss xmm0, dword[esp + 104]
	movss dword[esp + 16], xmm0
	mov dword[esp + 20], edx
	call _brl_max2d_DrawImageRect
	add esp, 24
	movdqu xmm4, dqword[esp]
	movdqu xmm5, dqword[esp + 16]
	movdqu xmm6, dqword[esp + 32]
	movdqu xmm7, dqword[esp + 48]
	add esp, 64
	ret 16

_BlitzMax_AssemblyEntry:
	mov eax, _%UninitedValues_Begin
	mov edx, _%UninitedValues_End - _%UninitedValues_Begin
	call _System_Memory_Zero%1
	call _BlitzMax_Main
	ret 

section ".data" data readable writeable align 32

	align 16
_%BlitzMax_AssemblyData:
	db 48 dup 0

section ".rdata" data readable align 32

	align 4
_%BlitzMax:
	dd _%BlitzMax_AssemblyData
	dd _%BlitzMax_GlobalPointers
	file "..\BlitzMax.zlib"

	align 4
_%BlitzMax_GlobalPointers:
	dd _bbGCStackTop
	dd _BlitzMax_Main
	dd _BlitzMax_Graphics%1
	dd _BlitzMax_CreateImage%1
	dd _BlitzMax_GetMousePosition
	dd _BlitzMax_ImageSize
	dd _BlitzMax_DrawImage%1
	dd _BlitzMax_DrawImageRect%1
	dd _BlitzMax_AssemblyEntry
	dd _%ZinniaCore

section ".bss" data readable writeable align 32
_%UninitedValues_Begin:

	align 32
_%UninitedValues_End:

