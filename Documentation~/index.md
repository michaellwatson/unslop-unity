# Unslop Unity Asset Bridge

Install via Unity Package Manager (disk or git URL). See the repository [README.md](../README.md).

## Guides

- [User guide](user-guide.md)
- [Artist guide](artist-guide.md)
- [Supported matrix](supported-matrix.md)

## User guide topics

1. Connect API key and bind project
2. Browse and install published assets
3. Publish a local prefab as a catalog asset / new version
4. Review staged updates before accept
5. Resolve material ownership conflicts
6. Set canonical scale / confirm scale in Unity
7. Rollback, pins, and drift repair

## Supported matrix

| Item | MVP |
|---|---|
| Unity | 6000.0+ |
| Render pipeline | URP (HDRP later) |
| Content | Static FBX + textures + materials.json |
| Auth | Bridge API key (`usk_…`) |
