# DeepSync Wearable Server

A TCP server for managing communication between DeepSync wearable devices and a client application. The server acts as a bidirectional bridge, collecting heart rate and sensor data from wearable devices and distributing it to connected applications while supporting command relay for device control.

## Features

- Simultaneous handling of wearable devices and client applications
- Bidirectional communication using TCP sockets
- Built-in simulation mode for testing without physical devices
- REST API endpoints for web-based monitoring and control
- Per-device LED color assignment via JSON configuration
- Stale device cleanup to maintain connection health
- Comprehensive logging with Serilog (console + file output)

## Requirements

- **.NET 8.0 SDK** or later
- **Operating System**: Windows, Linux, or macOS

## Dependencies

The project uses minimal external dependencies:

- **Serilog** (4.3.0) - Structured logging framework
- **Serilog.Sinks.Console** (6.1.1) - Console output
- **Serilog.Sinks.File** (7.0.0) - File-based logging
- **Serilog.Enrichers.** - Context enrichment for logging

All dependencies are automatically restored during build.

## Building

### Option 1: Visual Studio

1. Open `deepsyncwearablev2-server.sln` in Visual Studio 2022 or later
2. Build the solution using __Build > Build Solution__ or press `Ctrl+Shift+B`
3. Run the project with the desired command-line arguments via __Debug > Debug Properties__

### Option 2: .NET CLI

Restore dependencies, build and run: 

```
dotnet build
dotnet run -- <arguments>
```

Or build and run in release mode

```
dotnet build -c Release
dotnet run -c Release -- <arguments>
```

## Commandline Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--ip-app <IP>` | Network interface for application connections | `--ip-app 127.0.0.1` |
| `--port-app <PORT>` | Port for application data writer | `--port-app 43397` |
| `--ip-wearable <IP>` | Network interface for wearable connections | `--ip-wearable 127.0.0.1` |
| `--port-wearable <PORT>` | Port for wearable connections | `--port-wearable 53397` |
| `-d` | Enable simulated wearables mode | |

## Configuration

### Wearable Color Configuration

Create a `wearable_colors.json` file in the working directory to assign fixed LED colors.  
The server automatically applies these colors when wearables connect.

Example:

```json
[
    {
        "id": 1,
        "color":
        { 
            "r": 255, 
            "g": 0, 
            "b": 0 
        } 
    }, 
    { 
        "id": 2, 
        "color": 
        { 
            "r": 0, 
            "g": 255, 
            "b": 0 
        }
    }
]
```

## Network Interfaces

### Wearable Device Interface

**Default Port:** `53397`

Wearable devices connect to this port and send periodic updates containing:

- Device ID
- Heart rate (BPM)
- LED color state (RGB)
- Timestamp

The server can send commands back to wearables:

- Color change commands
- ID reassignment

### Application Interface

**Writer Port (Default):** `43397`  
**Reader Port (Default):** `43396`

- **Writer Port**: Server broadcasts wearable data to all connected applications
- **Reader Port**: Server receives control commands from applications to relay to wearables

### Frontend REST API

**Data Endpoint (Default):** `http://localhost:8788/api/wearables`  
**Simulated Endpoint (Default):** `http://localhost:8788/api/wearables/simulated`  
**Control Endpoint (Default):** `http://localhost:8790/api/control`

The server POSTs JSON data to these endpoints every 100ms for real-time dashboard updates.

## Logging

Logs are written to:

- **Console**: Real-time output with timestamp, log level, class, and method
- **File**: `logs/desy-server-<date>.log` (daily rolling files)

Log format:

`{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] ({ClassName}.{MethodName}) {Message}`

## License

MIT License - See [LICENSE](../LICENSE) for details.

## Credits

**Developed by:** Ars Electronica Futurelab

**Website:** https://ars.electronica.art/futurelab/
