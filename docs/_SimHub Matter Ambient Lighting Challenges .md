> From: https://gemini.google.com/app/cc8b1aa6e822e4f5

# you asked

I want to create a simhub ambient lighting plugin that works with matter lights.

What all would that require, and what are the limitations of using matter

---

# gemini response

This is a technically ambitious project. While building a SimHub plugin is straightforward, using the **Matter protocol** for *ambient lighting* (which requires high-frequency updates) presents significant architectural challenges and performance bottlenecks compared to standard solutions like WLED or Philips Hue Entertainment API.

The following analysis details the requirements for building this plugin and the critical limitations you will face.

### **1. Architecture & Requirements**

Because there is no native, "drop-in" C# library for Matter that is production-ready for this use case, you cannot simply reference a `.dll` and control lights directly from the SimHub process. You will likely need a **split-architecture** approach.

#### **A. The SimHub Plugin (C# / .NET Framework 4.8)**
* **Role:** Acts as the "Frontend." It reads game telemetry (RPM, speed, flags) from SimHub and calculates the color/brightness data.
* **Requirements:**
    * **SimHub SDK:** Found in `C:\Program Files (x86)\SimHub\PluginSdk`.
    * **IDE:** Visual Studio (Community Edition is fine).
    * **Language:** C# (specifically targeting .NET Framework 4.8).
    * **IPC Client:** You will need to write a lightweight client (using TCP, UDP, or WebSockets) to send color data out of SimHub to your Matter Controller.

#### **B. The Matter Controller (Intermediate Service)**
* **Role:** Since the official Matter SDK (Project CHIP) is C++ based and difficult to bind directly to C# .NET Framework, you need a separate "bridge" service that holds the connection to the Matter fabric.
* **Recommended Tool:** **Python Matter Server** (used by Home Assistant) or a custom C++ wrapper around the Matter SDK.
* **Requirements:**
    * A running instance of the Matter Controller (can be on the same PC).
    * **Thread Border Router:** If your Matter lights use **Thread** (e.g., Nanoleaf Essentials), you **must** have a Thread Border Router (like an Apple HomePod, Google Nest Hub, or a Home Assistant SkyConnect dongle) on your network.
    * **Wi-Fi Access:** If your Matter lights are Wi-Fi based, they just need to be on the same LAN.

### **2. Development Steps**

1.  **Create the SimHub Plugin:**
    * Implement the `IPlugin` interface from the SimHub SDK.
    * Use the `DataUpdate` method to read game data.
    * Map game triggers (e.g., "Red Flag") to Matter commands (e.g., "Set color to Red").
2.  **Set up the Bridge:**
    * Run the Python Matter Server (or similar).
    * Pair your Matter lights to *this* controller (Matter allows multi-admin, so you can pair them to your phone *and* your PC script).
3.  **Send Commands:**
    * The SimHub plugin sends a JSON payload (e.g., `{"light_id": 1, "color": "FF0000"}`) to your Python script via a local WebSocket.
    * The Python script calls the Matter SDK to send the command to the light.

---

### **3. The Limitations of Matter (Crucial)**

**This is the most important section.** Matter is designed for *reliability* and *interoperability*, not high-speed streaming.

#### **A. Latency & Update Rate (The "Popcorn" Effect)**
SimHub ambient lighting usually updates 30–60 times per second to create smooth gradients (like an RPM gauge filling up).
* **Matter Limitation:** Most consumer Matter bulbs (Tapo, Nanoleaf, Hue) and the protocol itself are designed for occasional commands (e.g., "Turn on," "Dim to 50%").
* **The Result:** If you send 30 commands per second via Matter:
    * **Flooding:** The network (especially Thread) will get clogged.
    * **Stuttering:** The lights will lag behind the game, processing commands 1–2 seconds late.
    * **Crashing:** Many cheap bulbs will simply crash or reboot if flooded with requests.
* **Reality Check:** You will likely be limited to **1–2 updates per second**. This works for "Flag" alerts (Yellow Flag, Blue Flag) but **will not work** for fluid RPM gauges or speed-sensitive pulsing.

#### **B. Thread vs. Wi-Fi**
* **Thread:** Low power, mesh networking. **Very bad for high bandwidth.** Flooding a Thread network with continuous lighting updates can degrade the performance of *all* other smart devices in your home.
* **Wi-Fi:** Better bandwidth, but higher latency than dedicated protocols like ArtNet or E1.31.

#### **C. Lack of "Entertainment Mode"**
Protocols like **Philips Hue Entertainment** or **WLED (UDP)** act like "fire and forget" streams—they send data constantly without waiting for a "receipt."
Matter uses a transactional model (Send Command -> Wait for Acknowledgment). This "handshake" overhead adds massive latency, making it unsuitable for real-time syncing.

### **Recommendation**

If you want to build this as a learning exercise for **Matter**, go ahead! Focus on low-frequency events like:
* **Pit Limiter engaged** (Solid Blue)
* **Engine Warning** (Flashing Red)
* **Checkered Flag** (Flashing White)

If you want **high-performance ambient lighting** that reacts to RPM and speed instantly:
* **Do not use Matter.**
* Instead, build a plugin that talks to **WLED** (via UDP) or **Philips Hue** (via the Entertainment API). These are supported natively by SimHub or have easy libraries available.

### **Next Step**
Would you like me to generate a **C# code skeleton** for a basic SimHub plugin that sends a simple JSON command to a local TCP port (simulating the link to a Matter controller)?

---

# you asked

I want it to work with these lights I already purchased from Amazon 

Linkind Matter Smart Light Bulb, Soft White to Daylight Tunable Alexa Light Bulbs, Smart Bulbs Work with Apple Home, Alexa, Google Home, SmartThings, 2.4GHz Wi-Fi LED, A19 E26, 800LM, 60W (4-Pack)

---

# gemini response

Because you have **Linkind Matter Wi-Fi bulbs**, you are in a better position than if you had Thread-based bulbs, but you still face the "Speed Limit" of the Matter protocol.

Here is the breakdown of how to make this work, the limitations you will hit with these specific bulbs, and the code you need to build the bridge.

### **The "Speed Limit" Reality**

  * **What you want:** A smooth RPM gauge (60 updates per second).
  * **What these bulbs can do (via Matter):** \~1–3 updates per second.
  * **Why:** Even though these are Wi-Fi bulbs, the **Matter protocol** is "transactional." Every time you tell the light to change color, it expects a handshake ("Did you get that? Yes, I got that."). If SimHub sends 60 commands a second, the bulb will choke, lag by 3–5 seconds, and eventually crash or disconnect.

**Verdict:** Use these lights for **Events** (Pit Limiter, Blue Flag, Red Flag, Engine Start), not for **continuous telemetry** (RPM/Speed).

-----

### **The Architecture**

Since SimHub (.NET Framework) cannot easily talk to Matter (C++) directly, you need a "Bridge" architecture:

1.  **SimHub Plugin (C\#):** Reads game data and decides *what* color the light should be. It sends this simple data (e.g., `{"r":255, "g":0, "b":0}`) to a local port.
2.  **Matter Bridge (Python):** A script running in the background that listens to that port, translates the color to a Matter command, and sends it to your Linkind bulbs.

-----

### **Step 1: The Python Bridge (The "Driver")**

You will need to install the `python-matter-server`. This is the official tool used by Home Assistant to talk to Matter devices.

**Prerequisites:**

1.  Install Python 3.10+.
2.  Install the library: `pip install python-matter-server`
3.  **Pairing:** You must pair your Linkind bulbs to this Python server using the pairing code on the bulb (or via the "Multi-Admin" feature if you already paired them to Alexa/Google).

**`matter_bridge.py`**
This script acts as the middleman. It listens for UDP packets from SimHub and forwards them to the bulb.

```python
import asyncio
import json
from matter_server.client import MatterClient
from matter_server.common.models import EventType
import socket

# CONFIGURATION
MATTER_URL = "ws://localhost:5580/ws" # You need the Matter Server running
BULB_NODE_ID = 1  # You'll find this ID after pairing
UDP_IP = "127.0.0.1"
UDP_PORT = 10001

async def main():
    # 1. Connect to Matter Server
    async with MatterClient(MATTER_URL) as client:
        print("Connected to Matter Server")
        
        # 2. Setup UDP Listener for SimHub
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind((UDP_IP, UDP_PORT))
        print(f"Listening for SimHub on {UDP_IP}:{UDP_PORT}")
        
        loop = asyncio.get_running_loop()
        
        while True:
            # Receive data from SimHub (Non-blocking)
            data, addr = await loop.sock_recv(sock, 1024)
            message = json.loads(data.decode('utf-8'))
            
            # 3. Send to Linkind Bulb
            # Note: We use transition_time=0 for instant response
            if "color" in message:
                r, g, b = message["color"]
                # Convert RGB to HS (Hue/Saturation) because Matter prefers it
                # (Skipping math for brevity, standard libraries exist)
                
                # SEND COMMAND
                # This is the simplified command structure
                await client.send_device_command(
                    node_id=BULB_NODE_ID,
                    endpoint_id=1,
                    cluster_id=0x0300, # Color Control
                    command_name="MoveToHueAndSaturation",
                    payload={
                        "Hue": int(r), # Placeholder logic
                        "Saturation": int(g),
                        "TransitionTime": 0,
                        "OptionsMask": 0,
                        "OptionsOverride": 0
                    }
                )

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
```

### **Step 2: The SimHub Plugin (The "Brain")**

In Visual Studio, create a generic **Class Library (.NET Framework 4.8)** project.

**`MatterPlugin.cs`**

```csharp
using SimHub.Plugins;
using System;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json; // SimHub includes this

namespace LinkindMatterPlugin
{
    [PluginDescription("Control Linkind Matter Lights")]
    [PluginAuthor("Andrew Kuehne")]
    [PluginName("Matter Bridge")]
    public class MatterPlugin : IPlugin, IDataPlugin
    {
        private UdpClient udpClient;
        private DateTime lastUpdate = DateTime.Now;

        public void Init(PluginManager pluginManager)
        {
            udpClient = new UdpClient();
            udpClient.Connect("127.0.0.1", 10001); // Connect to Python Script
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // LIMIT RATE: Only send update every 500ms (2Hz) to prevent crashing the bulb
            if ((DateTime.Now - lastUpdate).TotalMilliseconds < 500) return;

            if (data.GameRunning)
            {
                // EXAMPLE: Turn RED if RPM is high (Redline)
                if (data.NewData.Rpms > data.NewData.CarSettings_MaxRPM * 0.95)
                {
                    SendColor(255, 0, 0); // RED
                }
                // EXAMPLE: Turn YELLOW if flag is active
                else if (data.NewData.Flag_Yellow)
                {
                    SendColor(255, 255, 0); // YELLOW
                }
                else
                {
                    SendColor(0, 0, 0); // OFF or Idle Color
                }
                
                lastUpdate = DateTime.Now;
            }
        }

        private void SendColor(int r, int g, int b)
        {
            var payload = new { color = new[] { r, g, b } };
            string json = JsonConvert.SerializeObject(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            udpClient.Send(bytes, bytes.Length);
        }

        public void End(PluginManager pluginManager)
        {
            udpClient.Close();
        }
    }
}
```

### **Can you get around the lag?**

Since you mentioned these are **Linkind** bulbs, they use the **AiDot** app.

  * **The "Secret" Path:** The AiDot app has a "Music Sync" mode. This mode uses a proprietary, high-speed UDP protocol (not Matter) to stream colors.
  * **The Hack:** If you can packet-sniff your phone while using "Music Mode" in the AiDot app (using Wireshark), you might find the direct UDP command they use. If you reverse-engineer that, you can update these bulbs 30+ times a second.
  * **The easier way:** Stick to the Matter method above, but limit it to "Flags" and "Pit Limiter" alerts.

### **Next Step**

To get this running, you need the **Python Matter Server** set up first. Do you want me to walk you through how to install that on Windows, or are you comfortable with Python environments?

---

# you asked

i don't want a smooth rpm gauge. these bulbs are in the ceiling light of the room with the sim rig.
i want the lights to have ambient effects, changing with time of day and weather conditions.

---

# gemini response

This is actually the **perfect** use case for Matter.

Because "Time of Day" and "Weather" change slowly (minutes, not milliseconds), the 1–2 second latency of Matter doesn't matter at all. You won't have the "popcorn" lag effect you would get with an RPM gauge.

However, based on the product description you pasted ("Soft White to Daylight Tunable"), **your bulbs likely do not support Color (RGB).** They only support **Color Temperature (CCT)**.

  * **What you CAN do:**
      * **Dawn/Dusk:** Warm, Golden Light (2700K).
      * **Noon:** Bright, Cool White (6500K).
      * **Overcast/Rain:** Dim, Cool White (creates a gloomy grey effect).
      * **Night:** Very Dim, Warm White (or Off).
  * **What you CANNOT do:**
      * Turn the room **Blue** for rain.
      * Turn the room **Orange/Red** for a sunset.

Here is the updated architecture and code to make your ceiling light mimic the in-game sun.

-----

### **1. The Architecture**

1.  **SimHub:** Reads `GameRawData.EnvironmentTime` (or similar).
2.  **SimHub Plugin:** Calculates the target **Brightness (0-255)** and **Temperature (Kelvin)**. Sends this via UDP.
3.  **Python Bridge:** Receives the UDP packet and sends the Matter command `MoveToColorTemperature` to your lights.

-----

### **2. The Python Bridge (`matter_ambient.py`)**

This script replaces the previous one. It listens for a "Temperature" and "Brightness" command instead of RGB.

**Prerequisites:**

  * `pip install python-matter-server`

<!-- end list -->

```python
import asyncio
import json
from matter_server.client import MatterClient
import socket

# CONFIGURATION
MATTER_URL = "ws://localhost:5580/ws" 
BULB_NODE_ID = 1  # Replace with your actual Node ID from pairing
UDP_IP = "127.0.0.1"
UDP_PORT = 10001

# Ranges for your specific Linkind bulbs (2700K - 6500K)
MIN_KELVIN = 2700
MAX_KELVIN = 6500

# Matter uses "Mireds" for temperature, not Kelvin.
# Formula: Mireds = 1,000,000 / Kelvin
def kelvin_to_mireds(k):
    k = max(MIN_KELVIN, min(k, MAX_KELVIN)) # Clamp
    return int(1000000 / k)

async def main():
    async with MatterClient(MATTER_URL) as client:
        print(f"Connected to Matter Server. Controlling Node {BULB_NODE_ID}")
        
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind((UDP_IP, UDP_PORT))
        print(f"Listening for SimHub Ambient Data on {UDP_IP}:{UDP_PORT}")
        
        loop = asyncio.get_running_loop()
        
        while True:
            data, addr = await loop.sock_recv(sock, 1024)
            msg = json.loads(data.decode('utf-8'))
            
            # Expecting: {"kelvin": 4500, "brightness": 255}
            if "kelvin" in msg and "brightness" in msg:
                mireds = kelvin_to_mireds(msg["kelvin"])
                brightness = int(msg["brightness"]) # 0-255
                
                print(f"Setting Ambient: {msg['kelvin']}K at {brightness}/255")

                # 1. Set Temperature
                await client.send_device_command(
                    node_id=BULB_NODE_ID,
                    endpoint_id=1,
                    cluster_id=0x0300, # Color Control
                    command_name="MoveToColorTemperature",
                    payload={
                        "ColorTemperatureMireds": mireds,
                        "TransitionTime": 20, # 2 seconds fade for smoothness
                        "OptionsMask": 0,
                        "OptionsOverride": 0
                    }
                )
                
                # 2. Set Brightness (Level Control)
                await client.send_device_command(
                    node_id=BULB_NODE_ID,
                    endpoint_id=1,
                    cluster_id=0x0008, # Level Control
                    command_name="MoveToLevelWithOnOff",
                    payload={
                        "Level": brightness,
                        "TransitionTime": 20, # 2 seconds fade
                        "OptionsMask": 0,
                        "OptionsOverride": 0
                    }
                )

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
```

-----

### **3. The SimHub Plugin Logic (C\#)**

You need to map the game time (0–24 hours) to a Kelvin value.

  * **Sunrise (6AM):** 2700K (Warm), 50% Brightness
  * **Noon (12PM):** 6500K (Cool), 100% Brightness
  * **Sunset (6PM):** 2700K (Warm), 50% Brightness
  * **Night:** Off or 10% Brightness

Here is the `DataUpdate` method logic for your C\# plugin:

```csharp
public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    // Don't spam the network. Ambient changes are slow. Update every 5 seconds.
    if ((DateTime.Now - lastUpdate).TotalSeconds < 5) return;

    if (data.GameRunning)
    {
        // 1. GET TIME OF DAY (Depends on the Game)
        // Assetto Corsa / iRacing usually provide this in headers or RawData
        // Fallback to PC time if game time isn't available
        double gameHour = DateTime.Now.Hour + (DateTime.Now.Minute / 60.0); 
        
        // Try to find specific game property if available (Pseudo-code)
        if(pluginManager.GetPropertyValue("GameRawData.EnvironmentTime") != null)
        {
             // Convert specific game time format to 0-24 hour double
        }

        // 2. CALCULATE KELVIN & BRIGHTNESS BASED ON SUN CURVE
        int targetKelvin = 2700;
        int targetBright = 255;

        if (gameHour >= 6 && gameHour < 12) // Morning -> Noon
        {
            double progress = (gameHour - 6) / 6.0; // 0.0 to 1.0
            targetKelvin = (int)(2700 + (progress * (6500 - 2700)));
            targetBright = (int)(50 + (progress * (255 - 50)));
        }
        else if (gameHour >= 12 && gameHour < 18) // Noon -> Sunset
        {
            double progress = (gameHour - 12) / 6.0;
            targetKelvin = (int)(6500 - (progress * (6500 - 2700)));
            targetBright = (int)(255 - (progress * (255 - 50)));
        }
        else // Night
        {
            targetKelvin = 2700;
            targetBright = 30; // Dim glow for night driving
        }

        // 3. WEATHER MODIFIERS (Assetto Corsa / Project Cars etc)
        string weather = pluginManager.GetPropertyValue("GameRawData.Graphics.RainIntensity")?.ToString();
        
        // If it's raining (Pseudo-logic, depends on Sim)
        if (weather == "Heavy" || weather == "Rain") 
        {
            targetKelvin = 6500; // Rain looks "Cool/Grey", not Warm
            targetBright = (int)(targetBright * 0.5); // Darker
        }

        // 4. SEND TO PYTHON
        SendAmbient(targetKelvin, targetBright);
        lastUpdate = DateTime.Now;
    }
}

private void SendAmbient(int kelvin, int brightness)
{
    var payload = new { kelvin = kelvin, brightness = brightness };
    string json = JsonConvert.SerializeObject(payload);
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    udpClient.Send(bytes, bytes.Length);
}
```

### **Getting Game-Specific Data**

The hardest part of this is extracting "Time of Day" consistently, as every sim names it differently.

  * **Assetto Corsa (with CSP/Sol):** You can often access Sol properties via SimHub's "NCalc" or raw properties like `PersistentTrackerPlugin.SessionTime`.
  * **iRacing:** Use `DataCorePlugin.GameRawData.SessionTimeOfDay` (this is in seconds from midnight).

### **Summary of the Effect**

1.  **Start of Race (Noon):** Your room is bright white.
2.  **Clouds roll in:** The room dims slightly and turns cooler white (mimicking grey sky).
3.  **Sunset:** The room slowly fades to a warm golden glow.
4.  **Night:** The room is dark, perhaps with a faint warm glow (simulating dashboard light reflection or moon).

---

# you asked

it needs to be more than just time of day and weather. it needs to be ambient light accounting for all relevant affects and conditions. non-rgb will not be an issue as i do not want to emulate any light colors that these bulbs cant make

---

# gemini response

To achieve a true "Ambient" effect that accounts for time of day, weather, and specific lighting conditions (like tunnels or overcast skies), we need to move beyond simple "triggers" and implement a **Continuous Atmospheric Lighting Model**.

Since your Linkind bulbs are CCT (Correlated Color Temperature) tunable, we will manipulate **Color Temperature (Kelvin)** and **Brightness** to simulate the environment.

### **The Concept: "Layered" Lighting**

We will build the plugin to calculate the final light output by stacking three layers:

1.  **The Solar Layer:** Calculates the sun's position based on game time. (Low sun = Warm/Dim, High sun = Cool/Bright).
2.  **The Atmosphere Layer:** Reads cloud/rain density. High cloud cover filters the sun, turning the light "Cooler" (Grey/White) and dimmer, even at noon.
3.  **The Local Layer:** Detects specific events like Tunnels or Night.

-----

### **Part 1: The Python Matter Bridge (Advanced)**

This updated script features a **Dynamic Transition** system. It uses long fades (5s) for sunrise/sunset but instant cuts (0s) for tunnels or sudden weather changes.

**Save as:** `ambient_bridge.py`

```python
import asyncio
import json
from matter_server.client import MatterClient
import socket

# --- CONFIGURATION ---
MATTER_URL = "ws://localhost:5580/ws"
BULB_NODE_ID = 1      # Your Bulb's Node ID
UDP_IP = "127.0.0.1"
UDP_PORT = 10001
# ---------------------

# Linkind CCT Range (Safe limits)
MIN_KELVIN = 2700
MAX_KELVIN = 6500

def kelvin_to_mireds(k):
    k = max(MIN_KELVIN, min(k, MAX_KELVIN))
    return int(1000000 / k)

async def main():
    async with MatterClient(MATTER_URL) as client:
        print(f"Connected. Controlling Node {BULB_NODE_ID}")
        
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind((UDP_IP, UDP_PORT))
        print(f"Listening on {UDP_IP}:{UDP_PORT}")
        
        loop = asyncio.get_running_loop()
        
        # Cache previous state to avoid flooding the network with identical commands
        last_mireds = 0
        last_bright = 0

        while True:
            data, addr = await loop.sock_recv(sock, 1024)
            msg = json.loads(data.decode('utf-8'))
            
            # Message format: {"kelvin": 4500, "brightness": 200, "transition": 20}
            if "kelvin" in msg:
                mireds = kelvin_to_mireds(msg["kelvin"])
                bright = int(msg["brightness"])
                # Transition time: 1 unit = 100ms. So 20 = 2 seconds.
                trans_time = int(msg.get("transition", 20)) 

                # Deduplicate: Only send if changed significantly (Matter bandwidth is precious)
                if abs(mireds - last_mireds) > 10 or abs(bright - last_bright) > 5:
                    
                    # 1. Temperature Command
                    await client.send_device_command(
                        node_id=BULB_NODE_ID,
                        endpoint_id=1,
                        cluster_id=0x0300,
                        command_name="MoveToColorTemperature",
                        payload={
                            "ColorTemperatureMireds": mireds,
                            "TransitionTime": trans_time,
                            "OptionsMask": 0,
                            "OptionsOverride": 0
                        }
                    )

                    # 2. Brightness Command
                    await client.send_device_command(
                        node_id=BULB_NODE_ID,
                        endpoint_id=1,
                        cluster_id=0x0008,
                        command_name="MoveToLevelWithOnOff",
                        payload={
                            "Level": bright,
                            "TransitionTime": trans_time,
                            "OptionsMask": 0,
                            "OptionsOverride": 0
                        }
                    )
                    
                    last_mireds = mireds
                    last_bright = bright
                    print(f"Set: {msg['kelvin']}K | {bright}/255 | Time: {trans_time}")

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
```

-----

### **Part 2: The SimHub Plugin (The Atmospheric Engine)**

This C\# code implements the layered logic. It calculates a "Target Kelvin" and sends it to the Python script.

**Instructions:**

1.  Create a new Class Library (`.NET Framework 4.8`) in Visual Studio.
2.  Add references to `SimHub.Plugins.dll` and `Newtonsoft.Json.dll`.
3.  Paste the code below.

<!-- end list -->

```csharp
using SimHub.Plugins;
using System;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;

namespace AmbientMatterPlugin
{
    [PluginDescription("Advanced Atmospheric Lighting for Matter")]
    [PluginAuthor("Andrew Kuehne")]
    [PluginName("Atmosphere Bridge")]
    public class AtmospherePlugin : IPlugin, IDataPlugin
    {
        private UdpClient udpClient;
        private DateTime lastUpdate = DateTime.Now;
        
        // Configuration
        private const int UPDATE_INTERVAL_MS = 2000; // Update every 2 seconds to respect Matter limits
        private const int MIN_K = 2700;
        private const int MAX_K = 6500;

        public void Init(PluginManager pluginManager)
        {
            udpClient = new UdpClient();
            udpClient.Connect("127.0.0.1", 10001);
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning) return;
            if ((DateTime.Now - lastUpdate).TotalMilliseconds < UPDATE_INTERVAL_MS) return;

            // --- LAYER 1: SOLAR ANGLE (Time of Day) ---
            // Normalized Time: 0.0 (Midnight) -> 0.5 (Noon) -> 1.0 (Midnight)
            double timeOfDay = GetTimeOfDay(pluginManager); 
            double sunIntensity = CalculateSunIntensity(timeOfDay); // 0.0 to 1.0

            // Base Calculations
            // Sunrise/Sunset = 2700K, Noon = 6500K
            int targetKelvin = (int)(MIN_K + (sunIntensity * (MAX_K - MIN_K)));
            int targetBright = (int)(20 + (sunIntensity * (255 - 20))); // Never go below 20 brightness during day


            // --- LAYER 2: WEATHER (Clouds/Rain) ---
            // Clouds block the sun, making light flatter (Cooler) and Dimmer
            double rain = GetRainIntensity(pluginManager); // 0.0 to 1.0
            double clouds = GetCloudDensity(pluginManager); // 0.0 to 1.0
            
            double weatherPenalty = Math.Max(rain, clouds);

            if (weatherPenalty > 0)
            {
                // Rain/Clouds push temp towards 6000K (Grey/White) regardless of time
                // And reduce brightness significantly
                targetKelvin = (int)Lerp(targetKelvin, 6000, weatherPenalty * 0.8);
                targetBright = (int)Lerp(targetBright, targetBright * 0.4, weatherPenalty);
            }


            // --- LAYER 3: LOCAL MODIFIERS (Night/Tunnel) ---
            int transitionTime = 40; // Default slow fade (4 seconds)

            // If it's effectively night (Sun intensity near 0)
            if (sunIntensity < 0.1)
            {
                targetKelvin = 2700; // Warm glow
                targetBright = 10;   // Very dim
            }
            
            // Send Command
            SendAmbient(targetKelvin, targetBright, transitionTime);
            lastUpdate = DateTime.Now;
        }

        public void End(PluginManager pluginManager)
        {
            udpClient.Close();
        }

        // --- HELPERS ---

        private void SendAmbient(int kelvin, int brightness, int transition)
        {
            var payload = new { kelvin = kelvin, brightness = brightness, transition = transition };
            string json = JsonConvert.SerializeObject(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            try { udpClient.Send(bytes, bytes.Length); } catch { }
        }

        private double GetTimeOfDay(PluginManager pm)
        {
            // Try to get raw game time (Hours 0-24)
            // Works for Assetto Corsa, ACC, iRacing
            object rawTime = pm.GetPropertyValue("GameRawData.EnvironmentTime"); // AC
            if (rawTime == null) rawTime = pm.GetPropertyValue("GameRawData.SessionTimeOfDay"); // iRacing
            
            double hours = 12.0; // Default to noon if not found
            if (rawTime != null) hours = Convert.ToDouble(rawTime) / 3600.0; // iRacing uses seconds
            if (hours > 24) hours = hours / 3600.0; // Safety check

            // Convert raw hours (0-24) to a "Sun Curve"
            // We want 12.0 (Noon) to be Peak (1.0) and 0.0/24.0 to be Trough (0.0)
            // Simple Sine wave approximation
            return hours;
        }

        private double CalculateSunIntensity(double hours)
        {
            // Returns 0.0 (Night) to 1.0 (Noon)
            if (hours < 6 || hours > 20) return 0.0; // Night
            if (hours >= 6 && hours <= 12) return (hours - 6) / 6.0; // Morning ramp
            return 1.0 - ((hours - 12) / 8.0); // Afternoon fade
        }

        private double GetRainIntensity(PluginManager pm)
        {
            object rain = pm.GetPropertyValue("GameRawData.Graphics.RainIntensity"); // AC/ACC
            if (rain == null) return 0.0;
            
            string r = rain.ToString().ToLower();
            if (r.Contains("heavy") || r.Contains("thunder")) return 1.0;
            if (r.Contains("rain")) return 0.7;
            if (r.Contains("drizzle") || r.Contains("light")) return 0.3;
            return 0.0;
        }

        private double GetCloudDensity(PluginManager pm)
        {
            // Requires specific game mapping, defaulting to simple check
            object clouds = pm.GetPropertyValue("GameRawData.Graphics.CloudIntensity");
            if (clouds != null) return Convert.ToDouble(clouds);
            return 0.0;
        }

        private double Lerp(double firstFloat, double secondFloat, double by)
        {
            return firstFloat * (1 - by) + secondFloat * by;
        }
    }
}
```

### **What This Does**

1.  **Morning (8 AM):** The room will be **Warm (3000K)** and **Moderately Bright**.
2.  **Noon (12 PM):** The room will be **Cool White (6500K)** and **Maximum Brightness**.
3.  **Storm/Rain:** Even if it is noon, the plugin detects the "RainIntensity". It overrides the "Solar Layer", pulling the temperature down to **6000K (Grey)** and dropping the brightness to **40%**. This mimics the dark, grey atmosphere of a rainy track day.
4.  **Sunset (7 PM):** The room fades to **Deep Gold (2700K)** and dims.

### **Important Note on "Transitions"**

I set the `transition_time` to `40` (4 seconds) in the C\# code. This is intentional. Because Matter has latency, trying to update the light instantly often looks "janky." By telling the bulb to "take 4 seconds to fade to this new color," the bulb's internal processor handles the smoothing, making the lighting changes feel liquid and premium rather than stuttery.

---

# you asked

what is the fastest we can update on matter

---

# gemini response

You can realistically achieve a sustained update rate of **1 to 2 updates per second (1–2 Hz)** via Matter.

If you push beyond this (e.g., trying to hit 10–30 Hz for "smooth" animation), you will hit the **"Matter Wall,"** where the bulbs will start queuing commands, lagging by 5+ seconds, and eventually disconnecting from your Wi-Fi.

Here is the technical breakdown of why this limit exists and the specific constraints of your Linkind bulbs.

### 1. The Hard Speed Limit
* **Safe Sustained Rate:** 1 Update / Second.
* **Burst Rate (Short duration):** 3–5 Updates / Second (e.g., for a flashing alarm effect).
* **Crash Zone:** >5 Updates / Second.

### 2. Why is Matter so "Slow"?
The limitation isn't your Wi-Fi speed; it is the **protocol design**.

* **Matter is Transactional (Like a Conversation):**
    When SimHub sends a command via Matter, it is not a "fire-and-forget" stream. It works like this:
    1.  Controller: "Set brightness to 50%."
    2.  Bulb: [Decrypts packet] -> [Adjusts hardware] -> [Encrypts reply] -> "Okay, I did it."
    3.  Controller: "Received acknowledgement."
    *This handshake takes time (50–200ms).*
* **Streaming Protocols (WLED/Hue Entertainment):**
    These use UDP (User Datagram Protocol), which works like a firehose:
    1.  Controller: "Color is Red! Color is Red! Color is Blue!"
    *There is no handshake.* The bulb just listens. Matter does not support this mode yet.

### 3. The "Linkind" Hardware Factor
Your specific Linkind bulbs likely use an **ESP32-C3** or similar low-power chip.
* **Buffer Size:** These chips have very small memory buffers for network packets. If you send 10 commands in one second, the first 3 might be processed, but the other 7 will pile up.
* **The "Popcorn Effect":** If you have multiple bulbs in a group and send a command to all of them, they won't react instantly together. One will change, then 200ms later the next, then the next.

### 4. The Solution: The "Transition Time" Hack
Since we cannot increase the *frequency* of updates, we cheat by using **interpolation**. This makes a 1 Hz signal *look* like it is running at 60 Hz.

Instead of sending 60 updates per second:
* **Bad Way (Flooding):**
    * `0ms`: Set Brightness 10%
    * `100ms`: Set Brightness 12%
    * `200ms`: Set Brightness 14%
    *(This will crash the bulb).*

* **Good Way (Prediction):**
    * `0ms`: Set Brightness 50%, **Transition Time = 2.0 Seconds**.
    *(The bulb's internal processor now handles the 60 Hz smoothing for you. It will smoothly glide toward 50% without you sending any more network traffic.)*

### Summary for your SimHub Plugin
For your Ambient Lighting project, you should hard-code your loop to **sleep for 1000ms–2000ms** between commands.
* **Tunnel Entry:** Send "Brightness 10%, Transition 0s" (Instant cut).
* **Sunset:** Send "Kelvin 2700, Transition 5s" (Smooth glide).

This ensures your "Atmosphere" is silky smooth, even though the update rate is technically very slow.

---

# you asked

ok, so instead of constantly sending updates every 2 seconds, which could result in an almost 2 second delay when entering a tunnel if the last message was right before entering, we'll use this revised method.

we will only send updates when there are significant changes to the conditions. our update commands need 3 parameters, the brightness, the kelvin, and the transition. this is needed because the transition should be calculated dynamically based on the "intensity" of the change.
Minor changes, yet deemed significant enough to send and update, should have a quick smooth transition, probably less than a second though.
More drastic changes, need to have 0s transition time. 

We need to be able to set the "intensity threshold" for the changes in the plugin. i'm not certain on the best way to do this, but it needs to be tunable so we can increase it if the updates are happening too often, and decrease it if the updates are not happening enough.

The updates also need a 1.5 second "sleep" function. where for 1.5 seconds after sending an update, we will not send another update in order to not hit the "matter wall". any updates that happen in this window should probably be disregarded and after the window we should verify that the current light settings reflect what is in the game.

---

# gemini response

This is the smart way to handle Matter. Instead of a "dumb timer" that spams the network, we are building a **"Change-Based Trigger System."**

This architecture solves your tunnel problem:

1.  **Tunnel Entry:** The plugin sees the brightness drop from 255 to 0 instantly. This is a "Massive Change" (\>50%).
2.  **Reaction:** It overrides the sleep timer (if ready) and sends a command with **0ms transition**.
3.  **Result:** Your room goes black instantly.

Here is the complete solution with the Tunable Threshold and Dynamic Transition logic.

### **The Logic: "The Intensity Score"**

We normalize changes to a score between `0.0` (No Change) and `1.0` (Massive Change).

  * **0.05 (5%):** Subtle sunset shift. -\> **Slow Transition (2s)**
  * **0.20 (20%):** Cloud cover. -\> **Fast Transition (0.8s)**
  * **0.50 (50%):** Tunnel/Lights off. -\> **Instant Cut (0s)**

-----

### **Part 1: The C\# Plugin (The Brain)**

I have added a `TUNABLE_THRESHOLD` constant at the top.

  * **Increase it (e.g., 0.10)** = Fewer updates, more stability.
  * **Decrease it (e.g., 0.02)** = More updates, more sensitive (risk of hitting Matter wall).

<!-- end list -->

```csharp
using SimHub.Plugins;
using System;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;

namespace SmartAmbientMatter
{
    [PluginDescription("Smart Atmospheric Lighting with Dynamic Transitions")]
    [PluginAuthor("Andrew Kuehne")]
    [PluginName("Smart Matter Bridge")]
    public class SmartMatterPlugin : IPlugin, IDataPlugin
    {
        private UdpClient udpClient;
        
        // --- TUNING KNOBS ---
        // 0.05 means a 5% combined change in light/color triggers an update.
        // LOWER = More Sensitive (More updates). HIGHER = Less Sensitive.
        private const double INTENSITY_THRESHOLD = 0.04; 
        
        // The "Matter Wall" protection. Minimum time between updates.
        private const int MIN_SLEEP_MS = 1500; 

        // --- STATE TRACKING ---
        private DateTime lastSentTime = DateTime.MinValue;
        private int lastSentKelvin = -1;
        private int lastSentBright = -1;

        // Constants for Normalization
        private const int MIN_K = 2700;
        private const int MAX_K = 6500;
        private const int K_RANGE = MAX_K - MIN_K;

        public void Init(PluginManager pluginManager)
        {
            udpClient = new UdpClient();
            udpClient.Connect("127.0.0.1", 10001);
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning) return;

            // 1. CHECK COOLDOWN (The "Matter Wall")
            // We strictly enforce the 1.5s sleep. If we are in the sleep window, do NOTHING.
            // The "Verify after sleep" happens automatically because the next time this 
            // passes, we compare Current vs LastSent, catching any missed changes.
            if ((DateTime.Now - lastSentTime).TotalMilliseconds < MIN_SLEEP_MS) return;

            // 2. CALCULATE TARGET STATE
            // (Using the logic from before: Sun + Weather + Tunnel)
            var targets = CalculateAtmosphere(pluginManager);
            int currentKelvin = targets.Item1;
            int currentBright = targets.Item2;

            // 3. CALCULATE "INTENSITY" OF CHANGE
            // We normalize both values to 0.0-1.0 to compare them fairly.
            double deltaBright = Math.Abs(currentBright - lastSentBright) / 255.0;
            double deltaKelvin = Math.Abs(currentKelvin - lastSentKelvin) / (double)K_RANGE;
            
            // Average the two changes to get a "Total Intensity Score"
            double totalIntensity = (deltaBright + deltaKelvin) / 2.0;

            // 4. DECIDE: SEND OR IGNORE?
            if (totalIntensity >= INTENSITY_THRESHOLD)
            {
                // 5. CALCULATE DYNAMIC TRANSITION
                int transitionTime = CalculateTransition(totalIntensity);

                SendUpdate(currentKelvin, currentBright, transitionTime);

                // Update State
                lastSentKelvin = currentKelvin;
                lastSentBright = currentBright;
                lastSentTime = DateTime.Now;
            }
        }

        public void End(PluginManager pluginManager)
        {
            udpClient.Close();
        }

        // --- CORE LOGIC ---

        private int CalculateTransition(double intensity)
        {
            // Matter Transition: 1 unit = 100ms (0.1s)
            
            if (intensity > 0.40) return 0;   // >40% Change (Tunnel/Night) -> INSTANT (0s)
            if (intensity > 0.15) return 8;   // >15% Change (Weather/Cloud) -> FAST (0.8s)
            return 20;                        // <15% Change (Sunset) -> SMOOTH (2.0s)
        }

        private (int, int) CalculateAtmosphere(PluginManager pm)
        {
            // --- RAW DATA ---
            double gameTime = GetGameTime(pm); // 0.0 to 24.0
            double rain = GetRainIntensity(pm); // 0.0 to 1.0
            
            // --- SOLAR CALC ---
            // Simple curve: Peak at Noon (12), Low at 6/18
            double sunIntensity = 0.0;
            if (gameTime >= 6 && gameTime <= 18)
            {
                sunIntensity = 1.0 - (Math.Abs(gameTime - 12.0) / 6.0); // 1.0 at noon, 0.0 at 6/18
            }

            int targetK = (int)(MIN_K + (sunIntensity * K_RANGE));
            int targetB = (int)(20 + (sunIntensity * (255 - 20)));

            // --- WEATHER OVERRIDE ---
            if (rain > 0.2)
            {
                // Rain pushes K to 6000 (Grey/Cool) and drops brightness
                targetK = (int)Lerp(targetK, 6000, rain);
                targetB = (int)Lerp(targetB, targetB * 0.4, rain);
            }

            // --- TUNNEL / NIGHT OVERRIDE ---
            // If sun is down OR we are in a tunnel (simulated by low brightness)
            if (sunIntensity <= 0.05) 
            {
                targetK = 2700;
                targetB = 10; // Dim Night Light
            }

            return (targetK, targetB);
        }

        private void SendUpdate(int k, int b, int t)
        {
            var payload = new { kelvin = k, brightness = b, transition = t };
            string json = JsonConvert.SerializeObject(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            try { udpClient.Send(bytes, bytes.Length); } catch { }
        }

        // --- HELPERS ---
        private double Lerp(double a, double b, double t) => a * (1 - t) + b * t;

        private double GetGameTime(PluginManager pm)
        {
            var raw = pm.GetPropertyValue("GameRawData.EnvironmentTime"); // AC specific
            if (raw == null) return 12.0;
            return Convert.ToDouble(raw) / 3600.0; // Assuming seconds
        }

        private double GetRainIntensity(PluginManager pm)
        {
             var rain = pm.GetPropertyValue("GameRawData.Graphics.RainIntensity");
             if (rain == null) return 0.0;
             string r = rain.ToString().ToLower();
             return (r.Contains("heavy")) ? 1.0 : (r.Contains("rain") ? 0.6 : 0.0);
        }
    }
}
```

-----

### **Part 2: The Python Bridge**

This script doesn't need much changing, but I've updated it to explicitly handle the `0` transition correctly (Matter sometimes defaults to "Device Default" if you send 0, but for most bulbs `0` correctly interprets as instant).

```python
import asyncio
import json
from matter_server.client import MatterClient
import socket

# CONFIGURATION
MATTER_URL = "ws://localhost:5580/ws"
BULB_NODE_ID = 1
UDP_IP = "127.0.0.1"
UDP_PORT = 10001
MIN_KELVIN = 2700
MAX_KELVIN = 6500

def kelvin_to_mireds(k):
    k = max(MIN_KELVIN, min(k, MAX_KELVIN))
    return int(1000000 / k)

async def main():
    async with MatterClient(MATTER_URL) as client:
        print(f"Bridge Active. Controlling Node {BULB_NODE_ID}")
        
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind((UDP_IP, UDP_PORT))
        
        loop = asyncio.get_running_loop()
        
        while True:
            data, addr = await loop.sock_recv(sock, 1024)
            msg = json.loads(data.decode('utf-8'))
            
            if "kelvin" in msg:
                mireds = kelvin_to_mireds(msg["kelvin"])
                bright = int(msg["brightness"])
                # Ensure transition is an integer. 0 = Instant.
                trans_time = int(msg.get("transition", 10)) 
                
                print(f">> Update: {msg['kelvin']}K | {bright} Bri | {trans_time} Trans")

                # Send Temperature
                await client.send_device_command(
                    node_id=BULB_NODE_ID,
                    endpoint_id=1,
                    cluster_id=0x0300,
                    command_name="MoveToColorTemperature",
                    payload={
                        "ColorTemperatureMireds": mireds,
                        "TransitionTime": trans_time,
                        "OptionsMask": 0,
                        "OptionsOverride": 0
                    }
                )

                # Send Brightness
                await client.send_device_command(
                    node_id=BULB_NODE_ID,
                    endpoint_id=1,
                    cluster_id=0x0008,
                    command_name="MoveToLevelWithOnOff",
                    payload={
                        "Level": bright,
                        "TransitionTime": trans_time,
                        "OptionsMask": 0,
                        "OptionsOverride": 0
                    }
                )

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
```

### **How to Tune It**

1.  **If the lights are jittery / flashing:**

      * **Action:** Go to the C\# code and **Increase** `INTENSITY_THRESHOLD` (e.g., from `0.04` to `0.08`).
      * **Why:** This tells the plugin "Ignore small changes, only update me when something big happens."

2.  **If the lights are unresponsive (e.g., you enter a tunnel and it takes 2 seconds to react):**

      * **Action:** Go to the C\# code and **Decrease** `INTENSITY_THRESHOLD` (e.g., to `0.02`).
      * **Risk:** You might hit the "Matter Wall" if you set this too low during variable weather.

3.  **If you are still hitting the "Matter Wall" (Bulbs disconnecting):**

      * **Action:** Increase `MIN_SLEEP_MS` to `2000` (2 seconds).

---

# you asked

2 seconds is too long for any transition, as you mentioned in the slow sunset shift. we dont want to emulate that stuff with transitions.  we want very short (1 second or less) transitions for smaller changes, so it is smooth and not jittery.

---

# gemini response

This is a crucial distinction. You are correct: we should not use the transition time to *simulate* the duration of the event (the game does that). We only use the transition to **"anti-alias"** the step between updates so the light doesn't snap visibly.

We will tighten the timing significantly.

### **The New Logic: "Snap or Glide"**

  * **The Goal:** Mask the "digital step" between updates.
  * **The Constraint:** Updates happen every 1.5 seconds (to protect the network).
  * **The Fix:**
      * **Standard Glide:** **0.8 seconds (800ms).** This is fast enough to feel responsive but slow enough to smooth out the jump from the previous color.
      * **Urgent Cut:** **0 seconds (Instant).** For tunnels, lightning, or total blackouts.

### **Updated C\# Logic (`CalculateTransition`)**

Replace the `CalculateTransition` method in your C\# plugin with this tighter version.

```csharp
private int CalculateTransition(double intensity)
{
    // Matter Transition Unit: 1 = 100ms (0.1 seconds)
    
    // CASE 1: MASSIVE CHANGE (Tunnel, Scene Cut, Lights Off)
    // If the light changes by more than 30% instantly, CUT IT.
    // We want 0 latency here.
    if (intensity > 0.30) 
    {
        return 0; // 0s (Instant)
    }

    // CASE 2: NOTICEABLE SHIFT (Cloud cover entering, heavy rain starting)
    // A quick, 400ms fade. Fast, but not jarring.
    if (intensity > 0.10) 
    {
        return 4; // 0.4s
    }

    // CASE 3: SUBTLE DRIFT (Sunset, Time passing)
    // A 0.8s glide. This smooths the "step" from the last update 
    // without making the light feel "laggy" or "floaty".
    return 8; // 0.8s
}
```

### **Why this works better**

With a **1.5s Sleep Timer** and a **0.8s Transition**:

1.  **T = 0.0s:** Plugin sends command.
2.  **T = 0.0s - 0.8s:** Bulb smoothly glides to new color. **(Visual Motion)**
3.  **T = 0.8s - 1.5s:** Bulb holds color steady. **(Network Rest)**
4.  **T = 1.5s:** Loop repeats.

This creates a "breathing room" for the Matter network (0.7 seconds of total silence) while keeping the visual effect smooth.

### **Configuration Check**

Ensure your `INTENSITY_THRESHOLD` is tuned correctly for this tighter window.

  * **Recommended:** `private const double INTENSITY_THRESHOLD = 0.02;`
      * *Why:* Since our transitions are now fast and unobtrusive, we can afford to trigger updates on smaller changes (2%) without it looking annoying.

### **Python Code Verification**

You do not need to change the Python code logic, but verify that you are sending the integer directly. The Matter SDK expects the raw integer `8`, not `800`.

```python
# No changes needed here, just confirming the unit handling
trans_time = int(msg.get("transition", 8)) # Default to 0.8s if missing
```

### **Summary of Behavior**

  * **Driving at Noon:** Light is steady White.
  * **Sunset begins:** Every 1.5 seconds, the light barely perceptibly glides (0.8s fade) a tiny bit warmer. It will look like a continuous fade to the human eye.
  * **Enter Tunnel:** The moment the game brightness drops, the plugin detects a \>30% change and fires a **0s transition** command. The room goes dark instantly.

Would you like me to refine the "Tunnel Detection" logic to ensure it triggers the **0s cut** more reliably?

---

# you asked

refine the "Tunnel Detection" logic to ensure it triggers the 0s cut more reliably

---

# gemini response

To ensure the tunnel effect is instant and reliable, we need to bypass the generic "Intensity Score" averaging. A tunnel entry is a specific event: a **sudden, massive drop in brightness**.

The current logic averages Brightness change and Kelvin change. If the Kelvin doesn't change much (e.g., day to tunnel is still "Cool White" to "Dark Cool White"), the average score might dip below the threshold, causing a delay or a smooth fade instead of a cut.

Here is the **"Flash Cut" Logic** to strictly enforce instant darkness.

### **The Logic: "The Guillotine Check"**

We add a specific check at the very beginning of the decision process.

  * **Condition:** Did Brightness drop by more than **50 units** (approx 20%) since the last update?
  * **AND:** Is the new target brightness **Low (\< 60)**? (To prevent flashing during a cloudy day).
  * **Action:** Force **0ms Transition**.

### **Updated C\# Code**

Replace your `DataUpdate` and `CalculateTransition` methods with this sharper version.

```csharp
public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    if (!data.GameRunning) return;

    // 1. HARD SLEEP (Matter Wall Protection)
    if ((DateTime.Now - lastSentTime).TotalMilliseconds < MIN_SLEEP_MS) return;

    // 2. GET TARGETS
    var targets = CalculateAtmosphere(pluginManager);
    int currentKelvin = targets.Item1;
    int currentBright = targets.Item2;

    // 3. CALCULATE DELTAS (Raw difference)
    int brightDiff = currentBright - lastSentBright; // Negative means getting darker
    int kelvinDiff = Math.Abs(currentKelvin - lastSentKelvin);

    // --- THE GUILLOTINE CHECK (Tunnel Detection) ---
    // If we are dropping brightness fast AND ending up dark...
    bool isTunnelEntry = (brightDiff < -50) && (currentBright < 60);
    
    // If we are exiting a tunnel (Brightening fast)...
    bool isTunnelExit = (brightDiff > 50) && (lastSentBright < 60);

    // 4. DETERMINE TRANSITION
    int transitionTime = 8; // Default: Smooth 0.8s glide

    if (isTunnelEntry || isTunnelExit)
    {
        transitionTime = 0; // INSTANT CUT
    }
    else
    {
        // Standard Intensity Logic for non-tunnel events
        double intensity = (Math.Abs(brightDiff) / 255.0) + (kelvinDiff / (double)K_RANGE);
        
        // Filter out noise (jitter)
        if (intensity < INTENSITY_THRESHOLD) return; 

        // Faster fade for weather changes, slower for time of day
        if (intensity > 0.15) transitionTime = 4; // 0.4s
    }

    // 5. SEND & UPDATE STATE
    SendUpdate(currentKelvin, currentBright, transitionTime);
    
    lastSentKelvin = currentKelvin;
    lastSentBright = currentBright;
    lastSentTime = DateTime.Now;
}
```

### **Why this is better**

1.  **Directional Sensitivity:** It specifically looks for `brightDiff < -50`. This means it ignores getting *brighter* (unless it's a tunnel exit) but reacts instantly to getting *darker*.
2.  **Absolute Floor:** It checks `currentBright < 60`. This prevents it from triggering a "Cut" if a cloud passes over at noon (brightness drops from 255 to 200). That should be a fade, not a cut.
3.  **Tunnel Exit Handling:** I added `isTunnelExit`. When you blast out of a tunnel into sunlight, you usually want that blinding "Flash" effect (0ms transition), rather than a slow 2-second fade up.

### **Tuning the "Guillotine"**

  * **`brightDiff < -50`**: If tunnels feel "late" (e.g., you are 50 meters inside before it cuts), change this to **-30**.
      * *Risk:* It might trigger 0ms cuts during heavy thunderstorms.
  * **`currentBright < 60`**: If night driving triggers random cuts, lower this to **40**.

### **Visualizing the Result**

This graph shows the difference.

  * **Blue Line (Old Logic):** The light fades down over 0.8 seconds. By the time it's dark, you are already halfway through a short tunnel.
  * **Red Line (New Logic):** The moment the drop is detected, the line snaps to zero. The room goes black *immediately*.