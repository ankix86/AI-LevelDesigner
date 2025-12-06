using System;

[Serializable]
public class LevelData
{
    public Floor[] floors;
    public Stair[] stairs;
}

[Serializable]
public class Floor
{
    public string id;
    public Room[] rooms;
    public Connection[] connections;
    public Door[] doors;
    public Prop[] props;
}

[Serializable]
public class Room
{
    public string id;
    public string type;
    public float[] size;       // [width, depth, height]
    public float[] position;   // [x, z, y]
    public Element floor;
    public Element roof;
    public Element[] walls;
    public Door[] doors;       // auto openings
}

[Serializable]
public class Stair
{
    public string floor_from;
    public string floor_to;
    public string description;
    public float[] size;
    public float[] position;
}

[Serializable]
public class Connection
{
    public string from_room;
    public string to_room;
    public string from_floor;
    public string to_floor;
    public float[] location;
}

[Serializable]
public class Door
{
    public string id;
    public string from_room;
    public string to_room;
    public float[] size;        // [width, height, thickness]
    public float[] position;    // [xLocal, zLocal, bottomY]
    public float[] rotation;
}

[Serializable]
public class Prop
{
    public string id;
    public string type;
    public float[] size;
    public float[] position;
    public float[] rotation;
}

[Serializable]
public class Element
{
    public string id;
    public float[] size;
    public float[] position;
    public float[] rotation;
}
