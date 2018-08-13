
@echo OFF
setlocal

:: Source directory
set SOURCE_DIRECTORY=./inputs

:: Destination directory
set DESTINATION_DIRECTORY=./outputs

:: Directory for wav files
set WAV_DIRECTORY=./wav-audiosprites

:: Max audio sprite length (seconds)
set MAX_LENGTH=60


:: Calling AudioSpriter
call AudioSpriter.exe -s "%SOURCE_DIRECTORY%" -d "%DESTINATION_DIRECTORY%" -w "%WAV_DIRECTORY%" -max %MAX_LENGTH%