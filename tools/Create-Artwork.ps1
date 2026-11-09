param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach($bridgeSize in @(@(600,900),@(3840,1240),@(1280,720),@(920,430),@(512,512))){
 $bridgeWidth=$bridgeSize[0];$bridgeHeight=$bridgeSize[1]
 $bridgeBitmap=New-Object Drawing.Bitmap($bridgeWidth,$bridgeHeight)
 $bridgeGraphics=[Drawing.Graphics]::FromImage($bridgeBitmap)
 try{
  $bridgeGraphics.SmoothingMode='AntiAlias';$bridgeGraphics.TextRenderingHint='AntiAliasGridFit'
  $bridgeGraphics.Clear([Drawing.Color]::FromArgb(19,27,43))
  $bridgePen=New-Object Drawing.Pen([Drawing.Color]::FromArgb(96,208,198)),([single]($bridgeWidth/32))
  $bridgeAccent=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(96,208,198))
  try{
   $bridgeX=[single]($bridgeWidth*.2);$bridgeY=[single]($bridgeHeight*.28);$bridgeSpan=[single]($bridgeWidth*.6);$bridgeRise=[single]([Math]::Min($bridgeWidth,$bridgeHeight)*.21)
   $bridgeGraphics.DrawArc($bridgePen,$bridgeX,$bridgeY,$bridgeSpan,$bridgeRise*2,180,180)
   $bridgeGraphics.DrawLine($bridgePen,$bridgeX,$bridgeY+$bridgeRise,$bridgeX+$bridgeSpan,$bridgeY+$bridgeRise)
   $bridgeGraphics.FillEllipse($bridgeAccent,$bridgeX-$bridgeWidth*.04,$bridgeY+$bridgeRise-$bridgeWidth*.04,$bridgeWidth*.08,$bridgeWidth*.08)
   $bridgeGraphics.FillEllipse($bridgeAccent,$bridgeX+$bridgeSpan-$bridgeWidth*.04,$bridgeY+$bridgeRise-$bridgeWidth*.04,$bridgeWidth*.08,$bridgeWidth*.08)
  }finally{$bridgePen.Dispose();$bridgeAccent.Dispose()}
  if($bridgeWidth -ne 512){
   $bridgeFont=New-Object Drawing.Font('Segoe UI',([single]([Math]::Min($bridgeWidth*.09,$bridgeHeight*.13))),([Drawing.FontStyle]::Bold),([Drawing.GraphicsUnit]::Pixel))
   $bridgeBrush=New-Object Drawing.SolidBrush([Drawing.Color]::White)
   $bridgeFormat=New-Object Drawing.StringFormat;$bridgeFormat.Alignment='Center'
   try{$bridgeGraphics.DrawString('Codex', $bridgeFont,$bridgeBrush,[single]($bridgeWidth*.5),[single]($bridgeHeight*.68),$bridgeFormat)}finally{$bridgeFont.Dispose();$bridgeBrush.Dispose();$bridgeFormat.Dispose()}
  }
  $bridgeName=if($bridgeWidth -eq 512){'logo.png'}else{"${bridgeWidth}x${bridgeHeight}.png"}
  $bridgeBitmap.Save((Join-Path $OutputDirectory $bridgeName),[Drawing.Imaging.ImageFormat]::Png)
 }finally{$bridgeGraphics.Dispose();$bridgeBitmap.Dispose()}
}
# A PNG-backed 256px ICO avoids GDI icon handle ownership and contains only original artwork.
$bridgeImage=[Drawing.Image]::FromFile((Join-Path $OutputDirectory 'logo.png'))
$bridgeSmall=New-Object Drawing.Bitmap($bridgeImage,256,256)
$bridgeMemory=New-Object IO.MemoryStream
try{
 $bridgeSmall.Save($bridgeMemory,[Drawing.Imaging.ImageFormat]::Png);$bridgeBytes=$bridgeMemory.ToArray()
 $bridgeFile=[IO.File]::Create((Join-Path $OutputDirectory 'logo.ico'));$bridgeWriter=New-Object IO.BinaryWriter($bridgeFile)
 try{
  $bridgeWriter.Write([uint16]0);$bridgeWriter.Write([uint16]1);$bridgeWriter.Write([uint16]1)
  $bridgeWriter.Write([byte]0);$bridgeWriter.Write([byte]0);$bridgeWriter.Write([byte]0);$bridgeWriter.Write([byte]0)
  $bridgeWriter.Write([uint16]1);$bridgeWriter.Write([uint16]32);$bridgeWriter.Write([uint32]$bridgeBytes.Length);$bridgeWriter.Write([uint32]22);$bridgeWriter.Write($bridgeBytes)
 }finally{$bridgeWriter.Dispose();$bridgeFile.Dispose()}
}finally{$bridgeImage.Dispose();$bridgeSmall.Dispose();$bridgeMemory.Dispose()}
