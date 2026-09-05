using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class VideoReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public string senderIP = "0.0.0.0";
    public int port = 9999;

    [Header("UI Display")]
    public RawImage displayUI;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isRunning = false;

    // Thread-safe texture loading
    private byte[] latestFrameBytes;
    private bool hasNewFrame = false;
    private Texture2D videoTexture;
    private readonly object lockObject = new object();

    void Start()
    {
        // Initialize a texture placeholder
        videoTexture = new Texture2D(2, 2);
        if (displayUI != null)
        {
            displayUI.texture = videoTexture;
        }

        // Start network logic on a separate thread to prevent Unity from freezing
        isRunning = true;
        receiveThread = new Thread(ReceiveVideoData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void Update()
    {
        // Main thread updates the UI texture
        if (hasNewFrame)
        {
            lock (lockObject)
            {
                if (latestFrameBytes != null && latestFrameBytes.Length > 0)
                {
                    videoTexture.LoadImage(latestFrameBytes); // Decodes JPEG/PNG bytes automatically
                }
                hasNewFrame = false;
            }
        }
    }

    void ReceiveVideoData()
    {
        try
        {
            client = new TcpClient(senderIP, port);
            stream = client.GetStream();
            Debug.Log("Connected to video sender server.");

            byte[] sizeBuffer = new byte[8]; // Python "Q" format is an unsigned long long (8 bytes)

            while (isRunning)
            {
                // 1. Read the 8-byte payload size header
                ReadExact(stream, sizeBuffer, 8);

                // Convert bytes to UInt64 (equivalent to Python's struct.unpack("Q"))
                if (BitConverter.IsLittleEndian)
                {
                    // Python usually sends big-endian or system native. Adjust if needed.
                    // Array.Reverse(sizeBuffer); // Uncomment if sender is Big-Endian
                }
                ulong msgSize = BitConverter.ToUInt64(sizeBuffer, 0);

                // 2. Read the actual frame payload data
                byte[] frameBuffer = new byte[(int)msgSize];
                ReadExact(stream, frameBuffer, (int)msgSize);

                // 3. Pass frame bytes to the main thread safely
                lock (lockObject)
                {
                    latestFrameBytes = frameBuffer;
                    hasNewFrame = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Network Error: {e.Message}");
        }
    }

    // Helper method to guarantee full chunk reading from TCP stream
    private void ReadExact(NetworkStream stream, byte[] buffer, int size)
    {
        int totalRead = 0;
        while (totalRead < size)
        {
            int read = stream.Read(buffer, totalRead, size - totalRead);
            if (read == 0) throw new Exception("Connection lost while reading data stream.");
            totalRead += read;
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        stream?.Close();
        client?.Close();
        receiveThread?.Abort();
    }
}