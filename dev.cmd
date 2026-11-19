@echo off
rem Development shortcuts: dev run, dev test, dev release... (dev help lists them all).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\dev.ps1" %*
