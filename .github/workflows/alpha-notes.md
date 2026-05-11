### This 'alpha' release is an automatically generated snapshot of the current state of development. It is continuously updated with work-in-progress changes that may be broken, incomplete, or incompatible.

#### Supported RimWorld versions: latest 1.6

#### Installation
1. Download the `Multiplayer-beta.zip` file below.
2. Open your RimWorld installation directory
   * **Steam**: Right-click RimWorld in your Steam library → `Manage` → `Browse local Files`
   * Or navigate directly to:
     - Windows: `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods`
     - Mac: `~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods` (Right-click on `RimWorldMac.app`, then click `Show Package Contents` to see the `Mods` folder)
     - Linux: `~/.steam/steam/steamapps/common/RimWorld/Mods`
3. Extract the zip file into the `Mods` folder.
   * You should have a `Multiplayer` folder in the `Mods` folder (`Mods/Multiplayer`)
   * Make sure you do not have this directory structure: `Mods/Multiplayer-beta/Multiplayer`. If you do, move the `Multiplayer` folder to the parent directory.

---

#### Standalone server

Download `Server-beta.zip` if you want to host a dedicated standalone server for testing.

**Setup**
1. Download and extract `Server-beta.zip`.
2. Run `Server.exe` (Windows) or `dotnet Server.dll` (Linux/Mac) from the extracted folder.
3. The server will start and wait for the first connection.

**First-time configuration (bootstrap)**
No manual configuration files are required.
The **first player to connect** via the Multiplayer mod client will be prompted to perform the initial setup:
they can configure the game world, scenario, and other options directly from within RimWorld.
Subsequent players connect to the already-running session.