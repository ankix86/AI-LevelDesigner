🏗️ AI-Powered Procedural Building Generator for Unity

![Demo](https://github.com/ankix86/AI-LevelDesigner/blob/main/Video.gif)

Automatically build full environments using JSON created by an AI Level Architect

This system lets Unity construct complete indoor levels-rooms, floors, doors, walls, and roofs-entirely from AI-generated JSON.
You describe the building, the AI outputs the layout, and Unity generates the environment automatically.

📌 Overview

✨ The project transforms AI-written JSON into fully constructed 3D building interiors.\
🏠 Every room is generated with correct dimensions, walls, floors, and ceilings.\
🚪 Doors are automatically carved into walls and placed with proper rotation.\
🧱 Shared walls between rooms are detected and prevented.\
⬆️ Multi-floor support is built into the architecture for future expansion.\
🎨 Props and decorative elements can be added through the JSON schema.

🔍 How the System Works\
🧠 Step 1 - You describe a building to the AI

You tell the AI what kind of house or floorplan you want, such as “Create a 4BHK” or “Generate a layout with 2 bedrooms, 1 hall, 1 kitchen, 1 toilet.”\
The AI responds with a structured JSON layout following your strict schema.

AI PROMPT
```
You are an AI Level Architect for a Unity procedural building generator.
Your job is to output JSON EXACTLY in the following structure.
Do NOT add extra fields. Do NOT remove fields.
Follow this schema strictly every time:

{
  "floors": [
    {
      "id": "floor_1",
      "rooms": [
        {
          "id": "<room_id>",
          "type": "<type>",
          "size": [width, depth, height],
          "position": [x, z, y],
          "doors": [
            {
              "id": "<door_id>",
              "from_room": "<room_id>",
              "to_room": "<room_id>",
              "size": [w, d, h],
              "position": [x, z, y],
              "rotation": [pitch, yaw, roll]
            }
          ],
          "roof": {
            "id": "roof_<room_id>",
            "size": [width, depth, 0.1],
            "position": [0, 0, 1.45],
            "rotation": [0, 0, 0]
          }
        }
      ],
      "connections": [],
      "props": []
    }
  ],
  "stairs": []
}

Rules you must follow:

Every room must include: id, type, size, position, doors[], roof{}
Door positions must be in local room coordinates
Roof must always have height = 0.1, position = [0, 0, 1.45], rotation = [0, 0, 0]
Output must be valid JSON only
No comments, no explanations, no story text—only JSON

Now generate a layout for: <INSERT BUILDING DESCRIPTION HERE>
Example: 4BHK with hall, kitchen, 4 bedrooms, and 2 toilets.
```
📥 Step 2 - Unity reads the generated JSON

The JSON includes floors, rooms, door definitions, positions, sizes, and roofs.\
Unity parses the file and prepares to build the entire layout procedurally.

🏗️ Step 3 - The system constructs each room

Unity generates the room using the data provided.\
A floor plane is placed, a roof is positioned above it, and four walls are created based on the room’s width and depth.\
Doors listed in the JSON are matched to the correct walls.

🚪 Step 4 - Doors are carved into the walls

The system reads each door’s position, rotation, and size.\
It identifies which wall the door belongs to, removes that section of the wall, and inserts a door object.\
This creates clean, accurate openings between rooms.

🧱 Step 5 - Shared walls are removed

When two rooms share a boundary, the generator detects overlapping wall regions.\
Unity only builds a single shared wall instead of duplicates.\
This keeps geometry clean, optimized, and visually correct.

🏠 Step 6 - Roofs are added

Each room includes a roof entry which the generator uses to place a thin ceiling plane.\
This fully encloses the room from above.

🔮 Step 7 - Future expansion

The system is designed to grow without requiring changes to the JSON schema.\
Possible future features include window placement, multi-floor buildings with stairs, automatic props, and lighting automation.
