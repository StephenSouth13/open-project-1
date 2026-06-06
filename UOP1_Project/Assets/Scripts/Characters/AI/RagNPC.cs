using System;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UI;

[System.Serializable]
public class EmbedResponse { public EmbeddingData embedding; }
[System.Serializable]
public class EmbeddingData { public float[] values; }
[System.Serializable]
public class LoreChunk { public string text; public float[] vector; }
[System.Serializable]
public class GeminiResponse { public Candidate[] candidates; }
[System.Serializable]
public class Candidate { public Content content; }
[System.Serializable]
public class Content { public Part[] parts; }
[System.Serializable]
public class Part { public string text; }

// BẮT BUỘC GameObject này phải có component NPC đi kèm
[RequireComponent(typeof(NPC))]
public class RagNPC : MonoBehaviour
{
    [Header("Cấu hình API")]
    public string apiKey = "AQ.Ab8RN6K6jbPeZT8uHTPfdDweeXxQl4qtYrJIa3fVlShYVixBsw";
    private string embedUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";
    private string chatUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    [Header("Cơ sở dữ liệu (Vector Database)")]
    public List<LoreChunk> loreDatabase = new List<LoreChunk>();

    [Header("UI Tương tác")]
    public Text dialogueUIText;
    public GameObject dialoguePanel;

    [Header("Mô phỏng người chơi (Test Default)")]
    public string playerQuestion = "Ông có biết lũ quái vật kia từ đâu tới không?";

    // --- BIẾN QUẢN LÝ TRẠNG THÁI ---
    private bool isPlayerNear = false;
    private bool isTalking = false;
    private NPC npcComponent;

    void Start()
    {
        npcComponent = GetComponent<NPC>();

        if(dialoguePanel != null) dialoguePanel.SetActive(false);

        // --- CỐT TRUYỆN DÀNH RIÊNG CHO GAME CỦA BẠN ---
        // Nếu trên Inspector chưa nhập Lore, code sẽ tự động dùng bộ Lore gốc này.
        if (loreDatabase.Count == 0)
        {
            loreDatabase.Add(new LoreChunk { text = "Ngôi làng Chop Chop từng rất yên bình cho đến khi lũ quái vật tàn ác Xeno-Stalker từ trên trời giáng xuống tàn phá." });
            loreDatabase.Add(new LoreChunk { text = "Chiến binh Vanguard là niềm hy vọng duy nhất của chúng ta. Chỉ có sức mạnh của Vanguard mới đấm vỡ được lớp giáp của lũ Xeno." });
            loreDatabase.Add(new LoreChunk { text = "Ngoài lũ Xeno bay lượn trên không, quanh làng còn có những con Slime nhầy nhụa chuyên ăn cắp lương thực của dân làng." });
            loreDatabase.Add(new LoreChunk { text = "Phía sau rặng núi có một hang động, nghe đồn đó là nơi sào huyệt sinh ra lũ quái vật." });
        }

        StartCoroutine(InitializeDatabaseVectors());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
            Debug.Log("Nhấn SPACE để hỏi chuyện Trưởng Làng.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;

            if(dialoguePanel != null) dialoguePanel.SetActive(false);
            if(npcComponent != null) npcComponent.npcState = NPCState.Idle;
        }
    }

    private void Update()
    {
        if (isPlayerNear && Input.GetKeyDown(KeyCode.P) && !isTalking)
        {
            StartCoroutine(ProcessRagFlow(playerQuestion));
        }
    }

    IEnumerator GetEmbedding(string textToEmbed, System.Action<float[]> onSuccess)
    {
        string jsonBody = $@"{{
            ""model"": ""gemini-embedding-001"",
            ""content"": {{ ""parts"": [{{ ""text"": ""{textToEmbed}"" }}] }}
        }}";

        string requestUrl = $"{embedUrl.Trim()}?key={apiKey.Trim()}";

        using (UnityWebRequest req = new UnityWebRequest(requestUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                EmbedResponse res = JsonUtility.FromJson<EmbedResponse>(req.downloadHandler.text);
                onSuccess?.Invoke(res.embedding.values);
            }
        }
    }

    IEnumerator InitializeDatabaseVectors()
    {
        foreach (var chunk in loreDatabase)
        {
            if (chunk.vector == null || chunk.vector.Length == 0)
            {
                yield return StartCoroutine(GetEmbedding(chunk.text, (vector) => { chunk.vector = vector; }));
            }
        }
    }

    float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
        float dotProduct = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < vecA.Length; i++)
        {
            dotProduct += vecA[i] * vecB[i];
            normA += Mathf.Pow(vecA[i], 2);
            normB += Mathf.Pow(vecB[i], 2);
        }
        if (normA == 0 || normB == 0) return 0;
        return dotProduct / (Mathf.Sqrt(normA) * Mathf.Sqrt(normB));
    }

    IEnumerator ProcessRagFlow(string question)
    {
        isTalking = true;

        if(npcComponent != null) npcComponent.npcState = NPCState.Talk;

        if(dialoguePanel != null) dialoguePanel.SetActive(true);
        if(dialogueUIText != null) dialogueUIText.text = "Trưởng làng đang nhớ lại...";

        float[] questionVector = null;
        yield return StartCoroutine(GetEmbedding(question, (vec) => { questionVector = vec; }));

        if (questionVector == null)
        {
            isTalking = false;
            if(npcComponent != null) npcComponent.npcState = NPCState.Idle;
            yield break;
        }

        LoreChunk bestMatch = null;
        float highestScore = -1f;

        foreach (var chunk in loreDatabase)
        {
            float score = CalculateCosineSimilarity(questionVector, chunk.vector);
            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = chunk;
            }
        }

        // --- HỆ THỐNG PROMPT ĐƯỢC TỐI ƯU HÓA CHO NHẬP VAI ---
        string systemPrompt = $@"
                Ngươi là NPC Trưởng Làng trong một trò chơi sinh tồn. Ngôi làng của ngươi đang bị đe dọa.
                Người đang nói chuyện với ngươi là chiến binh Vanguard (người chơi).
                Hãy trả lời câu hỏi của Vanguard BẮT BUỘC DỰA TRÊN DỮ LIỆU SAU:
                '{bestMatch.text}'
                Quy tắc nghiêm ngặt:
                1. Chỉ trả lời ngắn gọn trong 2-3 câu, giọng điệu khẩn khoản, già nua.
                2. Nếu Vanguard hỏi những thứ hiện đại hoặc ngoài lề (như code, máy tính, đời thực), hãy nói: 'Ta già rồi, đầu óc lẩm cẩm, ta không hiểu ngài Vanguard đang nói gì. Xin hãy cứu làng!'.
                3. Tuyệt đối không bịa thêm tình tiết ngoài dữ liệu được cung cấp.
                ";

        string jsonBody = $@"{{
            ""systemInstruction"": {{ ""parts"": [{{ ""text"": ""{systemPrompt}"" }}] }},
            ""contents"": [{{ ""parts"": [{{ ""text"": ""{question}"" }}] }}]
        }}";
        string requestChatUrl = $"{chatUrl.Trim()}?key={apiKey.Trim()}";

        using (UnityWebRequest req = new UnityWebRequest(requestChatUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                GeminiResponse res = JsonUtility.FromJson<GeminiResponse>(req.downloadHandler.text);
                if (res != null && res.candidates != null && res.candidates.Length > 0)
                {
                    string npcAnswer = res.candidates[0].content.parts[0].text;
                    if(dialogueUIText != null) dialogueUIText.text = npcAnswer;
                }
            }
            else
            {
                if(dialogueUIText != null) dialogueUIText.text = "Xin lỗi Vanguard, ta đang hơi mệt... (Lỗi API)";
            }
        }

        isTalking = false;
    }
}
