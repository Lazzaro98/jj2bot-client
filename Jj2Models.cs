using System;
using System.Net;

namespace Jj2
{
    /// <summary>
    /// Stores server metadata learned from the JJ2 TCP and UDP protocol.
    /// </summary>
    struct Server
    {
        public IPAddress ip;
        public int port;
        public string location;
        public bool priv;
        public int gameMode;
        public string gameModeStr;
        public int maxScore;
        public string version;
        public DateTime startTime;
        public int playerCount;
        public int maxPlayers;
        public string levelName;
        public uint levelCrc;
        public uint tilesetCrc;
        public string serverName;
        public string tileSetNamme;
        public int customMode;
        public int startHealth;
        public int maxHealth;
        public int plusOnly;
        public int friendlyFire;
        public int noMovement;
        public int noBlink;
        public int numOfPlayers;

        /// <summary>
        /// Initializes server metadata with JJ2 protocol defaults.
        /// </summary>
        public Server(IPAddress ip, int port)
        {
            this.ip = ip;
            this.port = port;
            location = "";
            priv = false;
            gameMode = -1;
            gameModeStr = "";
            maxScore = 5;
            version = "24  ";
            startTime = DateTime.Now;
            playerCount = 0;
            maxPlayers = 32;
            serverName = "";
            levelName = "";
            levelCrc = 0;
            tilesetCrc = 0;
            tileSetNamme = "";
            customMode = 0;
            startHealth = 3;
            maxHealth = 3;
            plusOnly = 0;
            friendlyFire = 0;
            noMovement = 0;
            noBlink = 0;
            numOfPlayers = 1;
        }
    }

    /// <summary>
    /// Stores the protocol state for a player visible to this client.
    /// </summary>
    struct Player
    {
        public byte sockID;
        public byte playerID;
        public int pchar;
        public int pTeam;
        public int[] fur;
        public string pName;
        public bool connected;
        public bool IsSpectating;

        /// <summary>
        /// Initializes a disconnected player entry with the supplied display name.
        /// </summary>
        public Player(string name)
        {
            sockID = 0x00;
            playerID = 0x00;
            pchar = 0;
            pTeam = 0;
            pName = name;
            fur = new int[4];
            connected = false;
            IsSpectating = false;
        }
    }
}
