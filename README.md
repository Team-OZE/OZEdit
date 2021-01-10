# 📦 OZEdit

![App Build](https://github.com/Team-OZE/OZEdit/workflows/App%20Build/badge.svg?branch=main)

OZEdit is a tool created by the OZE dev. Team to package/unpackage Warcraft III map files.

There are two versions, one with a graphical interface for Windows, one which is a command line utility (currently working on Windows only).

You can get them from the [Releases Section](https://github.com/Team-OZE/OZEdit/releases).

## ➡️ How to use (GUI)

TBR

## ➡️ How to use (command line)

As of now, the syntax is very simple.

`./OZEdit.exe <source folder> <destination file>`

Example : `./OZEdit.exe C:\MapFiles C:\MyMap.w3x`

> Path have to be absolute.

## ➡️ Pipelines

The project is currently being used with Github Actions automated pipelines by the Map project. It automates the map's build from source folder.

See an example here https://github.com/Team-OZE/LegionTD-Map/blob/main/.github/workflows/map-bundling.yml 
The pipeline download the tool, extract it, use it to compile the MPQ/w3x, and upload the created map file as a Github artifact.

## 💪 How to contribute

Follow the [Installation.md](Installation.md) instructions.
