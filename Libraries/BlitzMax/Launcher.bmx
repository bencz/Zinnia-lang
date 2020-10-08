Strict

Framework Brl.StandardIO
Import Brl.Max2D
Import Brl.GLMax2D
Import Brl.GLGraphics
Import BRL.PNGLoader
Import BRL.D3D9Max2D

Function DebugPrintString(Str:Byte Ptr)
	Str :+ 16
	Local Length = (Int Ptr(Str))[0]
	Str :+ 4
	
	Local Temp:Short Ptr = Short Ptr(MemAlloc((Length + 1) * 2))
	MemCopy Temp, Str, Length * 2
	Temp[Length] = 0
	Print String.FromWString(Temp)
	MemFree Temp
EndFunction

Function DebugPrintInt(x)
	Print x
EndFunction

Function FunctionCallTest(x, y, z, w)
	Print "x = " + x
	Print "y = " + y
	Print "z = " + z
	Print "w = " + w
EndFunction

Global Ms
Function BeginPerf()
	Ms = MilliSecs()
EndFunction

Function EndPerf()
	Ms = MilliSecs() - Ms
	Print "Time: " + String(Float(Ms) / 1000#)
EndFunction

Global PrevMs, Frames, FPS
Function DrawFrameStats()
	Local CurrentMS = MilliSecs()
	If Abs(CurrentMS - PrevMS) > 1000 
		PrevMS = CurrentMS
		FPS = Frames
		Frames = 0
	EndIf
	
	DrawText "FPS: " + FPS, 5, 5
	'DrawText "Frames: " + Frames, 5, 25
	'DrawText "PrevMS: " + PrevMS, 5, 45
	'DrawText "CurrentMS: " + CurrentMS, 5, 65
	'DrawText "CurrentMS - PrevMS: " + (CurrentMS - PrevMS), 5, 85
	Frames :+ 1
EndFunction