using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TeacherInformation : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] TMP_Text text;
    [SerializeField] TMP_Text roomTitle;
    [SerializeField] TMP_Text status;
    [SerializeField] ScrollRect scroll;
    const string BaseUrl = "https://www.htl-salzburg.ac.at";
    HttpClient client;
    readonly List<Collider2D> rooms = new List<Collider2D>();
    string currentRoom = "";
    int requestVersion;

    void Awake()
    {
        var issuer = Resources.Load<TextAsset>("Certificates/GlobalSignAlphaSSL2025");
        if (issuer == null) throw new InvalidOperationException("Das SSL-Zwischenzertifikat fehlt im Projekt.");
        var root = Resources.Load<TextAsset>("Certificates/GlobalSignRootR46");
        if (root == null) throw new InvalidOperationException("Das SSL-Stammzertifikat fehlt im Projekt.");
        client = SchoolHttp.CreateClient(issuer.bytes, root.bytes);
    }

    void Start() { panel.SetActive(false); }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!Regex.IsMatch(collision.name, @"^[BC]\s?\d{3}$")) return;
        if (!rooms.Contains(collision)) rooms.Add(collision);
        ShowRoom(collision.name);
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if (!rooms.Remove(collision)) return;
        if (rooms.Count > 0) ShowRoom(rooms[rooms.Count - 1].name);
        else CloseRoom();
    }

    public void CloseRoom()
    {
        ++requestVersion;
        currentRoom = "";
        if (panel) panel.SetActive(false);
    }

    void Update() { if (Input.GetKeyDown(KeyCode.Escape)) CloseRoom(); }

    async void ShowRoom(string room)
    {
        room = NormalizeRoom(room);
        if (currentRoom == room) return;
        currentRoom = room;
        int version = ++requestVersion;
        panel.SetActive(true);
        roomTitle.text = "Raum " + room;
        status.text = "Lehrkräfte & Sprechstunden";
        SetText("Lehrkräfte werden geladen …");
        try
        {
            string listUrl = BaseUrl + "/lehrerinnen.html";
            Debug.Log($"[Test6] [Lehrerdaten] Request-Start | Raum={room} | Typ=Übersicht | URL={listUrl}", this);
            string listHtml = await client.GetStringAsync(listUrl);
            Debug.Log($"[Test 4] Geladene URL: {listUrl} | Antwortlänge: {listHtml.Length}"); // <-- Hier einfach diese Zeile dazuschreiben
            if (!this || version != requestVersion) return;

            var links = TeacherParser.ParseTeacherLinks(listHtml, room);
            var teachers = new List<TeacherData>();
            int failed = 0;
            foreach (string link in links)
            {
                if (!this || version != requestVersion) return;
                try
                {
                    var uri = new Uri(new Uri(BaseUrl), link);
                    if (uri.Scheme != "https" || uri.Host != "www.htl-salzburg.ac.at") continue;
                    Debug.Log($"[Lehrerdaten] Request-Start | Raum={room} | Typ=Lehrerdetails | URL={uri}", this);
                    string detailHtml = await client.GetStringAsync(uri);
                    Debug.Log($"[Test 3] Geladene URL: {uri} | Antwortlänge: {detailHtml.Length}");
                    var teacher = TeacherParser.ParseTeacherDetails(detailHtml);

                    if (NormalizeRoom(teacher.roomNumber) == room) teachers.Add(teacher);
                }
                catch (Exception ex) { ++failed; Debug.LogWarning(ex.ToString()); }
            }
            teachers.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            var result = new StringBuilder();
            foreach (var teacher in teachers)
            {
                result.Append("<b><size=24>").Append(Safe(teacher.name)).Append("</size></b>\n");
                result.Append("<color=#167D8D>Sprechstunde</color>\n").Append(Safe(teacher.consultationHours)).Append("\n");
                result.Append("<size=18><color=#526478>Tel-DW: ").Append(Safe(teacher.phone)).Append("\n");
                result.Append(Safe(teacher.email)).Append("</color></size>\n\n");
            }
            if (teachers.Count == 0) result.Append(failed > 0 ? "Die Lehrerdaten konnten nicht vollständig geladen werden. Bitte den Raum erneut betreten." : "Für diesen Raum sind keine Lehrkräfte in der Schulübersicht hinterlegt.");
            else if (failed > 0) result.Append("<size=18>Einige Einträge konnten nicht geladen werden. Bitte später erneut versuchen.</size>");
            if (!this || version != requestVersion) return;
            string body = result.ToString();
            status.text = teachers.Count + " Lehrkräfte · Sprechstunden laut Schulwebsite";
            SetText(body);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(ex.ToString());
            Debug.Log("[Test7] was passiert wenn die Übersicht nicht erreichbar ist");
            if (this && version == requestVersion)
            {
                status.text = "Verbindung zur Schulwebsite fehlgeschlagen";
                SetText("Die Lehrerdaten sind gerade nicht erreichbar.\n\nPrüfe die Internetverbindung und betritt den Raum erneut.");
            }
        }
    }

    void SetText(string value)
    {
        text.text = value;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
        if (scroll)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)scroll.transform);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }
    }
    static string Safe(string value) => string.IsNullOrWhiteSpace(value) || value == "-" ? "Nicht angegeben" : value.Replace("<", "‹").Replace(">", "›");
    static string NormalizeRoom(string room) => Regex.Replace((room ?? "").ToUpperInvariant(), @"[\s_-]", "");
    void OnDestroy() { ++requestVersion; client?.Dispose(); }
}
