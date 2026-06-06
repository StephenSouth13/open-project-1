using TMPro;
using System;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// --- CẤU TRÚC JSON CHO API ---
[System.Serializable] public class EmbedResponse { public EmbeddingData embedding; }
[System.Serializable] public class EmbeddingData { public float[] values; }
[System.Serializable] public class GeminiResponse { public Candidate[] candidates; }
[System.Serializable] public class Candidate { public Content content; }
[System.Serializable] public class Content { public Part[] parts; }
[System.Serializable] public class Part { public string text; }
[System.Serializable] public class LoreChunk { public string text; public float[] vector; }

public class RagNPC : MonoBehaviour
{
    [Header("Cấu hình API")]
    public string apiKey = "AQ.Ab8RN6KB4oNWLFDjtiU3olJdhpQTa55qp9giz6zuYGSZsTIFNw";
    private string embedUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";
    private string chatUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    [Header("Cơ sở dữ liệu (Vector Database)")]
    public List<LoreChunk> loreDatabase = new List<LoreChunk>();

    [Header("UI Tương tác (Kéo thả từ Inspector)")]
    public Text dialogueUIText;
    public GameObject dialoguePanel;
    public TMP_InputField playerInputField; // 🎯 KHUNG NHẬP TEXT

    [Header("Mô phỏng người chơi (Test nhanh)")]
    public string playerQuestion = "Truyền thuyết về vũ khí tối thượng nằm ở đâu?";

    private bool isPlayerNear = false;
    private bool isTalking = false;
    private bool isTyping = false; // 🛡️ Trạng thái chặn xung đột bàn phím
    private NPC npcComponent;

    void Start()
    {
        npcComponent = GetComponent<NPC>();
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        // Giấu khung nhập text đi, chờ bấm E mới mở
        if (playerInputField != null)
        {
            playerInputField.gameObject.SetActive(false);
            // Lắng nghe sự kiện khi người chơi bấm Enter
            playerInputField.onSubmit.AddListener(OnSubmitQuestion);
        }

        if (loreDatabase.Count == 0)
        {
          loreDatabase.Add(new LoreChunk
			{
			text = "Thần kiếm Moonfang được cho là đang ngủ dưới hồ Silverlake."
			});

			loreDatabase.Add(new LoreChunk
			{
			text = "Ngục tối Blackstone nằm sâu trong rừng Sương Mù."
			});

			loreDatabase.Add(new LoreChunk
			{
			text = "Hiệp sĩ Aldric từng đánh bại Rồng Đỏ bằng lòng kiên trì."
			});

			loreDatabase.Add(new LoreChunk
			{
			text = "Quán rượu Con Quạ Say là nơi các nhà thám hiểm thường tụ họp."
			});

			loreDatabase.Add(new LoreChunk
			{
			text = "Kho báu của vua Arcturus được cho là nằm phía sau Thác Trăng."
			});

			loreDatabase.Add(new LoreChunk
			{
			text = "Phù thủy Merrow ghét cà rốt dù chẳng ai biết lý do."
			});
        }

        StartCoroutine(InitializeDatabaseVectors());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") || other.name.Contains("InteractionDetector"))
        {
            isPlayerNear = true;
            // Chỉ hiện hướng dẫn nếu không đang chat và không đang gõ chữ
            if (!isTalking && !isTyping)
            {
                if (dialoguePanel != null) dialoguePanel.SetActive(true);
                if (dialogueUIText != null) dialogueUIText.text = "Thần đằng BABIBON [Nhấn E để tương tác].";
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") || other.name.Contains("InteractionDetector"))
        {
            isPlayerNear = false;

            // Lỡ đang gõ mà bỏ chạy thì tắt khung gõ
            if (isTyping)
            {
                isTyping = false;
                if (playerInputField != null) playerInputField.gameObject.SetActive(false);
            }

            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (npcComponent != null) npcComponent.npcState = NPCState.Idle;
        }
    }

    private void Update()
    {
        // Nhận phím E CHỈ KHI đang ở gần, chưa nói chuyện, và CHƯA MỞ KHUNG GÕ CHỮ
        if (isPlayerNear && !isTalking && !isTyping)
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                isTyping = true; // Bật khiên chặn lỗi

                if (dialogueUIText != null) dialogueUIText.text =
    "Người lữ khách muốn hỏi điều gì? Hãy nói, ta sẽ lắng nghe.";

                if (playerInputField != null)
                {
                    playerInputField.gameObject.SetActive(true);
                    playerInputField.ActivateInputField(); // 🎯 ÉP CHUỘT VÀO KHUNG, KHÓA BÀN PHÍM GAME
                }
            }
        }
    }

    // 🎯 Hàm này tự chạy khi bạn gõ xong và bấm Enter
    private void OnSubmitQuestion(string question)
    {
        // Nếu lỡ bấm Enter mà chưa gõ gì -> Dẹp, trở về như cũ
        if (string.IsNullOrWhiteSpace(question))
        {
            isTyping = false;
            if (playerInputField != null) playerInputField.gameObject.SetActive(false);
            if (dialogueUIText != null) dialogueUIText.text = "Nhấn [E] để xin 3 chữ cái từ Lốp Trưởng.";
            return;
        }

        // Bắt đầu xử lý API
        isTyping = false;
        if (playerInputField != null)
        {
            playerInputField.text = ""; // Xóa trắng ô
            playerInputField.gameObject.SetActive(false); // Cất ô đi
        }

        StartCoroutine(ProcessRagFlow(question));
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
            yield return StartCoroutine(GetEmbedding(chunk.text, (vector) => { chunk.vector = vector; }));
        }
    }

    float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
        if (vecA == null || vecB == null || vecA.Length != vecB.Length) return 0;

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
        if (npcComponent != null) npcComponent.npcState = NPCState.Talk;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueUIText != null) dialogueUIText.text =
    "Thần Đằng BaBiBon đang lục tìm những ký ức cổ xưa...";

        float[] questionVector = null;
        yield return StartCoroutine(GetEmbedding(question, (vec) => { questionVector = vec; }));

        if (questionVector == null)
        {
            isTalking = false;
            if (npcComponent != null) npcComponent.npcState = NPCState.Idle;
            if (dialogueUIText != null) dialogueUIText.text =
    "Các linh hồn cổ đại đang ngủ. Hãy thử lại sau.";
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

        // 🎭 SYSTEM PROMPT SIÊU LẦY: LỐP TRƯỞNG CHỨNG KHOÁN
        string systemPrompt = $@"
Ngươi là Thần Đằng BaBiBon.

Ngươi là một hiền giả cổ đại sống trong thế giới fantasy trung cổ.

TÍNH CÁCH:
- Hài hước.
- Thông thái.
- Có chút cà khịa nhẹ.
- Nói chuyện thân thiện.
- Không sử dụng từ hiện đại.
- Không nhắc tới internet.
- Không nhắc tới AI.
- Không nhắc tới chứng khoán.

NGỮ CẢNH:
{bestMatch.text}

QUY TẮC:
- Trả lời từ 2 đến 6 câu.
- Nếu biết câu trả lời trong ngữ cảnh thì trả lời theo ngữ cảnh.
- Nếu không biết thì trả lời bằng một câu chuyện ngắn hoặc lời khuyên vui vẻ.
- Luôn giữ bầu không khí phiêu lưu trung cổ.
- Không được tự bịa các địa điểm ngoài ngữ cảnh.
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
                string npcAnswer = res.candidates[0].content.parts[0].text;

                if (dialogueUIText != null) dialogueUIText.text = npcAnswer;
            }
            else
            {
                if (dialogueUIText != null) dialogueUIText.text = "ÁI chà, câu hỏi dễ vậy cũng hỏi. Về suy nghĩ thêm đi!";
            }
        }

        isTalking = false;

        // Đặt lại trạng thái khi nói xong để chuẩn bị chat lại
        StartCoroutine(ResetDialogueState());
    }

    // Đợi 4 giây sau khi nói xong để reset lại hướng dẫn
    IEnumerator ResetDialogueState()
    {
        yield return new WaitForSeconds(15f);
        if (isPlayerNear && !isTalking && !isTyping)
        {
            if (dialogueUIText != null) dialogueUIText.text = "Nhấn [E] để xin tham khảo từ thầN Đằng BaBiBon.";
        }
        else if (!isPlayerNear)
        {
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
        }
    }
}
