# EFT DMA Radar

## Description

EFT DMA Radar is a radar tool designed for Escape from Tarkov that provides real-time tracking of players and items on a 2D map.

## Project Structure

- **Maps**: Directory containing maps data.
- **Source**: Source code directory.
  - **Tarkov**: Directory for Tarkov-related files.
    - **ExfilManager.cs**: Manages extraction points.
    - **Game.cs**: Handles game-related functionalities.
    - **GearManager.cs**: Manages player gear. (Work in Progress)
    - **GrenadeManager.cs**: Handles grenade-related functionalities.
    - **LootManager.cs**: Manages loot items. (Work in Progress - loot tracking works but need to create cache so it would automatically refresh current game loot)
    - **Player.cs**: Manages player-related functionalities. (Work in Progress - need to fix gear and health managers)
    - **RegisteredPlayers.cs**: Manages registered players.
    - **TarkovMarketManager.cs**: Manages Tarkov market-related operations.
  - **Misc**: Directory for miscellaneous files.
    - **Extensions.cs**: Contains extension methods.
    - **Misc.cs**: Contains miscellaneous functionalities.
    - **Offsets.cs**: Contains memory offsets.
    - **SKPaints.cs**: Contains SKPaint configurations.

## Usage

1. Clone the repository.
2. Ensure all necessary dependencies are in place.
3. Compile the project.
4. Run the application.

## Dependencies

- FTD3XX.dll
- leechcore.dll
- vmm.dll

## Contact

For any inquiries or assistance, feel free to contact me on Discord:

[keeegi]: keeegi_10477

## Note

Ensure all necessary files are properly included and referenced for the application to function correctly.
