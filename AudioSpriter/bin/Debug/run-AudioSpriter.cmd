
@echo OFF
setlocal

:: Source directory
set SOURCE_DIRECTORY=./inputs

:: Destination directory
set DESTINATION_DIRECTORY=./outputs

:: Directory for wav files
set WAV_DIRECTORY=./wav-audiosprites

:: Location of the ffmpeg file
set FFMPEG_FILE=./ffmpeg.exe

:: Max audio sprite length (seconds)
set MAX_LENGTH=60


:: Calling AudioSpriter
call AudioSpriter.exe -s "%~dp0%SOURCE_DIRECTORY%" -d "%~dp0%DESTINATION_DIRECTORY%" -w "%~dp0%WAV_DIRECTORY%" -ff "%~dp0%FFMPEG_FILE%" -max %MAX_LENGTH%