# WimReader

Console application to list, extract, and read files from WIM archives stored on network shares without transferring the entire WIM file.

*SCCM* looking at you :)

## Usage

### List files in a WIM archive

```
wimreader.exe --list --image <wim-path> [--path <directory-path>]
```

### Extract a file from a WIM archive

```
wimreader.exe --extract --image <wim-path> <source-path> <dest-path>
```

### Read file content (output to stdout)

```
wimreader.exe --read --image <wim-path> <source-path>
```

## Examples

List all files in root directory:
```
wimreader.exe --list --image "\\server\share\image.wim"
```

List files in a specific directory:
```
wimreader.exe --list --image "\\server\share\image.wim" --path "Windows\System32"
```

Extract a file:
```
wimreader.exe --extract --image "\\server\share\image.wim" "Windows\System32\notepad.exe" "C:\output\notepad.exe"
```

Read a text file (outputs directly):
```
wimreader.exe --read --image "\\server\share\image.wim" "Windows\System32\drivers\etc\hosts"
```

Read a binary file (outputs as Base64):
```
wimreader.exe --read --image "C:\image.wim" "Windows\System32\notepad.exe"
```

## Notes

- The application reads WIM files incrementally, only transferring metadata and requested files
- Automatically searches all images if file not found in first image
- Network paths (UNC) are supported
- Binary files are automatically detected and output as Base64 when using --read

## Credits
Lefty @ 2026