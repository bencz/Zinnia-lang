	format	MS COFF
	extrn	___bb_blitz_blitz
	extrn	___bb_d3d9max2d_d3d9max2d
	extrn	___bb_glmax2d_glmax2d
	extrn	___bb_max2d_max2d
	extrn	___bb_pngloader_pngloader
	extrn	___bb_standardio_standardio
	extrn	_bbIntAbs
	extrn	_bbMemAlloc
	extrn	_bbMemCopy
	extrn	_bbMemFree
	extrn	_bbMilliSecs
	extrn	_bbStringClass
	extrn	_bbStringConcat
	extrn	_bbStringFromFloat
	extrn	_bbStringFromInt
	extrn	_bbStringFromWString
	extrn	_brl_max2d_DrawText
	extrn	_brl_standardio_Print
	public	__bb_main
	public	_bb_BeginPerf
	public	_bb_DebugPrintInt
	public	_bb_DebugPrintString
	public	_bb_DrawFrameStats
	public	_bb_EndPerf
	public	_bb_FPS
	public	_bb_Frames
	public	_bb_FunctionCallTest
	public	_bb_Ms
	public	_bb_PrevMs
	section	"code" code
__bb_main:
	push	ebp
	mov	ebp,esp
	cmp	dword [_44],0
	je	_45
	mov	eax,0
	mov	esp,ebp
	pop	ebp
	ret
_45:
	mov	dword [_44],1
	call	___bb_blitz_blitz
	call	___bb_max2d_max2d
	call	___bb_glmax2d_glmax2d
	call	___bb_pngloader_pngloader
	call	___bb_d3d9max2d_d3d9max2d
	call	___bb_standardio_standardio
	mov	eax,0
	jmp	_24
_24:
	mov	esp,ebp
	pop	ebp
	ret
_bb_DebugPrintString:
	push	ebp
	mov	ebp,esp
	push	ebx
	push	esi
	push	edi
	mov	esi,dword [ebp+8]
	add	esi,16
	mov	edi,dword [esi]
	add	esi,4
	mov	eax,edi
	add	eax,1
	shl	eax,1
	push	eax
	call	_bbMemAlloc
	add	esp,4
	mov	ebx,eax
	mov	eax,edi
	shl	eax,1
	push	eax
	push	esi
	push	ebx
	call	_bbMemCopy
	add	esp,12
	mov	word [ebx+edi*2],0
	push	ebx
	call	_bbStringFromWString
	add	esp,4
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	push	ebx
	call	_bbMemFree
	add	esp,4
	mov	eax,0
	jmp	_27
_27:
	pop	edi
	pop	esi
	pop	ebx
	mov	esp,ebp
	pop	ebp
	ret
_bb_DebugPrintInt:
	push	ebp
	mov	ebp,esp
	mov	eax,dword [ebp+8]
	push	eax
	call	_bbStringFromInt
	add	esp,4
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	mov	eax,0
	jmp	_30
_30:
	mov	esp,ebp
	pop	ebp
	ret
_bb_FunctionCallTest:
	push	ebp
	mov	ebp,esp
	push	ebx
	push	esi
	push	edi
	mov	eax,dword [ebp+8]
	mov	esi,dword [ebp+12]
	mov	ebx,dword [ebp+16]
	mov	edi,dword [ebp+20]
	push	eax
	call	_bbStringFromInt
	add	esp,4
	push	eax
	push	_18
	call	_bbStringConcat
	add	esp,8
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	push	esi
	call	_bbStringFromInt
	add	esp,4
	push	eax
	push	_19
	call	_bbStringConcat
	add	esp,8
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	push	ebx
	call	_bbStringFromInt
	add	esp,4
	push	eax
	push	_20
	call	_bbStringConcat
	add	esp,8
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	push	edi
	call	_bbStringFromInt
	add	esp,4
	push	eax
	push	_21
	call	_bbStringConcat
	add	esp,8
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	mov	eax,0
	jmp	_36
_36:
	pop	edi
	pop	esi
	pop	ebx
	mov	esp,ebp
	pop	ebp
	ret
_bb_BeginPerf:
	push	ebp
	mov	ebp,esp
	call	_bbMilliSecs
	mov	dword [_bb_Ms],eax
	mov	eax,0
	jmp	_38
_38:
	mov	esp,ebp
	pop	ebp
	ret
_bb_EndPerf:
	push	ebp
	mov	ebp,esp
	sub	esp,4
	call	_bbMilliSecs
	sub	eax,dword [_bb_Ms]
	mov	dword [_bb_Ms],eax
	mov	eax,dword [_bb_Ms]
	mov	dword [ebp+-4],eax
	fild	dword [ebp+-4]
	fdiv	dword [_61]
	sub	esp,4
	fstp	dword [esp]
	call	_bbStringFromFloat
	add	esp,4
	push	eax
	push	_22
	call	_bbStringConcat
	add	esp,8
	push	eax
	call	_brl_standardio_Print
	add	esp,4
	mov	eax,0
	jmp	_40
_40:
	mov	esp,ebp
	pop	ebp
	ret
_bb_DrawFrameStats:
	push	ebp
	mov	ebp,esp
	push	ebx
	call	_bbMilliSecs
	mov	ebx,eax
	mov	eax,ebx
	sub	eax,dword [_bb_PrevMs]
	push	eax
	call	_bbIntAbs
	add	esp,4
	cmp	eax,1000
	jle	_49
	mov	dword [_bb_PrevMs],ebx
	mov	eax,dword [_bb_Frames]
	mov	dword [_bb_FPS],eax
	mov	dword [_bb_Frames],0
_49:
	push	1084227584
	push	1084227584
	push	dword [_bb_FPS]
	call	_bbStringFromInt
	add	esp,4
	push	eax
	push	_23
	call	_bbStringConcat
	add	esp,8
	push	eax
	call	_brl_max2d_DrawText
	add	esp,12
	add	dword [_bb_Frames],1
	mov	eax,0
	jmp	_42
_42:
	pop	ebx
	mov	esp,ebp
	pop	ebp
	ret
	section	"data" data writeable align 8
	align	4
_44:
	dd	0
	align	4
_bb_Ms:
	dd	0
	align	4
_bb_PrevMs:
	dd	0
	align	4
_bb_Frames:
	dd	0
	align	4
_bb_FPS:
	dd	0
	align	4
_18:
	dd	_bbStringClass
	dd	2147483647
	dd	4
	dw	120,32,61,32
	align	4
_19:
	dd	_bbStringClass
	dd	2147483647
	dd	4
	dw	121,32,61,32
	align	4
_20:
	dd	_bbStringClass
	dd	2147483647
	dd	4
	dw	122,32,61,32
	align	4
_21:
	dd	_bbStringClass
	dd	2147483647
	dd	4
	dw	119,32,61,32
	align	4
_61:
	dd	0x447a0000
	align	4
_22:
	dd	_bbStringClass
	dd	2147483647
	dd	6
	dw	84,105,109,101,58,32
	align	4
_23:
	dd	_bbStringClass
	dd	2147483647
	dd	5
	dw	70,80,83,58,32
