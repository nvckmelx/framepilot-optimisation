@echo off
rem Builds and tests FramePilot, then publishes a self-contained build to artifacts\FramePilot.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" %*
