using UnityEngine;

// Minimal stub to satisfy project reference for the missing Readme asset.
// Matches the structure of Unity's default Readme ScriptableObject.
public class Readme : ScriptableObject
{
    public Texture2D icon;
    public string title;
    public Section[] sections;
    public bool loadedLayout;

    [System.Serializable]
    public class Section
    {
        public string heading;
        public string text;
        public string linkText;
        public string url;
    }
}
