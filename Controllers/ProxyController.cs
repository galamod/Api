using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using NUglify;
using System.Text;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProxyController> _logger;
        private const string TargetBaseUrl = "https://galaxy.mobstudio.ru/web/";

        public ProxyController(IHttpClientFactory httpClientFactory, ILogger<ProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet]
        [Route("{*path}")]
        public async Task<IActionResult> Get(string path = "")
        {
            var client = _httpClientFactory.CreateClient();
            var targetUrl = string.IsNullOrEmpty(path) ? new Uri(TargetBaseUrl) : new Uri(new Uri(TargetBaseUrl), path);

            try
            {
                var response = await client.GetAsync(targetUrl);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                }

                var contentType = response.Content.Headers.ContentType?.ToString();

                if (contentType != null && contentType.Contains("text/html"))
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var scriptNodes = doc.DocumentNode.SelectNodes("//script");
                    if (scriptNodes != null)
                    {
                        foreach (var script in scriptNodes.ToList())
                        {
                            var src = script.GetAttributeValue("src", string.Empty);
                            if (script.InnerHtml.Contains("serviceWorker.register") || src.Contains("sw.js") || src.Contains("service-worker"))
                            {
                                script.Remove();
                                _logger.LogInformation("������ ������ Service Worker.");
                            }
                        }
                    }

                    var head = doc.DocumentNode.SelectSingleNode("//head");
                    if (head != null)
                    {
                        var baseTag = doc.CreateElement("base");
                        baseTag.SetAttributeValue("href", TargetBaseUrl);
                        head.PrependChild(baseTag);
                    }

                    var body = doc.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        // --- ������ ��������� ---

                        // 1. ��������� ��� ������ �� �����
                        var script = @"(function () {
    try {
        if (window.__ws_hooked) return;
        window.__ws_hooked = true;

        const OriginalWebSocket = window.WebSocket;
        const originalSend = WebSocket.prototype.send;

        window.WebSocket = function (url, protocols) {
            const socket = new OriginalWebSocket(url, protocols);
            let gameRun = false;
            let gameBotId = null;
            let myPosition = null;
            let nextPosition = null;
            let myWeapons = 3;
            let nextWeapons = 3;
            let prizePosition = null;
            let light = false;
            let action = false;
            let isLoadBall = false;
            let ballValue = localStorage.getItem('ballValue') || 'fire';
            let pauseStart = localStorage.getItem('pauseStart') || ""03:11"";
            let pauseEnd = localStorage.getItem('pauseEnd') || ""09:30"";
            let greenLamp = localStorage.getItem('greenLamp') || false;
            let autoPause = localStorage.getItem('autoPause') || false;
            let isPaused = false;

            function resetGameState() {
                gameRun = false;
                gameBotId = null;
                myPosition = null;
                nextPosition = null;
                myWeapons = 3;
                nextWeapons = 3;
                prizePosition = null;
                light = false;
                action = false;
                isLoadBall = false;
                greenLamp = false;
                autoPause = false;
                isPaused = false;
            }

            const views = new Map();

            class ViewsObject {
                constructor(name, id, i1, i2, i3) {
                    this.name = name;
                    this.id = id;
                    this.i1 = i1;
                    this.i2 = i2;
                    this.i3 = i3;
                }
            }

            const users = new Map();

            class User {
                constructor({ id, nick, clan, position, owner, stars, join }) {
                    this.id = id;
                    this.nick = nick;
                    this.clan = clan;
                    this.position = position;
                    this.owner = owner;
                    this.stars = stars;
                    this.join = join;
                }
            }

            class Bot {
                constructor() {
                    if (Bot._instance) {
                        return Bot._instance;
                    }

                    this.reset();
                    Bot._instance = this;
                }

                static getInstance() {
                    if (!Bot._instance) {
                        Bot._instance = new Bot();
                    }
                    return Bot._instance;
                }

                reset() {
                    this.nick = '';
                    this.pass = '';
                    this.id = '';
                    this.currentPlanet = '';
                }
            }

            const bot = Bot.getInstance();

            class CodeBlockTracker {
                constructor(interval, maxHits) {
                    this.interval = interval; // � �������������
                    this.maxHits = maxHits;
                    this.codeBlockHits = new Map();
                }

                trackCodeBlock(codeBlockId) {
                    const currentTime = Date.now();

                    if (!this.codeBlockHits.has(codeBlockId)) {
                        this.codeBlockHits.set(codeBlockId, []);
                    }

                    // �������� ���������� �������
                    let timestamps = this.codeBlockHits.get(codeBlockId).filter(t => currentTime - t <= this.interval);
                    timestamps.push(currentTime);
                    this.codeBlockHits.set(codeBlockId, timestamps);

                    // �������� ���������� ������������
                    if (timestamps.length >= this.maxHits) {
                        this.codeBlockHits.set(codeBlockId, []);
                        return true;
                    }

                    return false;
                }

                countHits(codeBlockId) {
                    return this.codeBlockHits.has(codeBlockId) ? this.codeBlockHits.get(codeBlockId).length : 0;
                }

                clearHits(codeBlockId) {
                    this.codeBlockHits.set(codeBlockId, []);
                }
            }

            let executionQueue = [];
            let tracker = new CodeBlockTracker(60000, 10);

            socket.send = function (data) {
                console.log(""���������� ���������:"", data);

                const parts = data.split(/\s+/i);

                switch (parts[0]) {
                    case ""USER"":
                        console.log(`��������� USER: ${parts[1]} ${parts[2]} ${parts[3]}`);
                        bot.id = parts[1];
                        bot.pass = parts[2];
                        bot.nick = parts[3];
                        break;
                    default:
                        console.log(`��������� ���������: ${data}`);
                        break;
                }

                return originalSend.call(this, data);
            };

            socket.addEventListener(""close"", (event) => {
                console.log(""ws_close"", { code: event.code, reason: event.reason });
            });

            socket.addEventListener(""error"", (error) => {
                console.log(""ws_error"", { error: error.message });
            });

            socket.addEventListener(""open"", () => {
                console.log('log', { message: ""Connection opened. Official cannon created by ����"" });
                resetGameState();
                bot.reset();
            });

            socket.addEventListener(""message"", (event) => {
                const input = event.data.trim();
                const parts = input.split(/\s+/i);
                const info = input.includes(':') ? input.substring(input.indexOf(':') + 1) : null;

                switch (parts[0]) {
                    case ""REGISTER"":
                        console.log(`REGISTER: ${parts[1]} ${parts[2]} ${parts[3]}`);
                        bot.id = parts[1];
                        bot.pass = parts[2];
                        bot.nick = parts[3];
                        break;
                    case ""VIEW_SCRIPT"":
                        viewScript(input, parts);
                        break;
                    case ""ADD_VIEW"":
                        addView(input, parts);
                        break;
                    case ""PART"":
                    case ""SLEEP"":
                        userExit(parseInt(parts[1]));
                        break;
                    case ""JOIN"":
                        userJoin(input, parts[2], parts[3]);
                        break;
                    case ""REMOVE"":
                        remove(parts);
                        break;
                    case "":srv"":
                        srv(input, info);
                        break;
                    case ""ACTION"":
                        act(input, info);
                        break;
                    case "":adv"":
                        adv(input);
                        break;
                    case ""353"":
                        parser353(info);
                        break;
                    case ""850"":
                        closeDialogs();
                        break;
                    case ""855"":
                        users.clear();
                        views.clear();
                        tracker.clearHits(""GreenLight"");
                        break;
                    case ""900"":
                        handle900(parts);
                        break;
                }

                switch (parts[1]) {
                    case ""KICK"":
                    case ""BAN"":
                    case ""PRISON"":
                        userExit(parseInt(parts[1]));
                        break;
                }
            });

            const settings = {
                greenLamp: greenLamp,
                ballValue: ballValue,
                pauseStart: pauseStart,
                pauseEnd: pauseEnd,
                autoPause: autoPause
            };

            // ������ ������� �������-������
            const sideTab = document.createElement('div');
            sideTab.style.position = 'fixed';
            sideTab.style.right = '0';
            sideTab.style.top = '50%';
            sideTab.style.transform = 'translateY(-50%)';
            sideTab.style.width = '40px';
            sideTab.style.height = '40px';
            sideTab.style.backgroundColor = '#3B4252';
            sideTab.style.borderRadius = '8px 0 0 8px';
            sideTab.style.display = 'flex';
            sideTab.style.alignItems = 'center';
            sideTab.style.justifyContent = 'center';
            sideTab.style.cursor = 'pointer';
            sideTab.style.zIndex = '1000';
            sideTab.style.boxShadow = '-2px 0 8px rgba(0, 0, 0, 0.2)';
            sideTab.style.transition = 'all 0.3s ease';
            sideTab.style.overflow = 'hidden';

            // ��������� ������� � ������
            const tabContent = document.createElement('div');
            tabContent.style.display = 'flex';
            tabContent.style.flexDirection = 'column';
            tabContent.style.alignItems = 'center';
            tabContent.style.gap = '5px';
            tabContent.style.color = '#88C0D0';
            tabContent.innerHTML = `
                <div style=""font-size: 24px;"">??</div>
            `;
            sideTab.appendChild(tabContent);

            // ������� ��������� ��� ������
            sideTab.addEventListener('mouseenter', () => {
                sideTab.style.width = '45px';
                sideTab.style.backgroundColor = '#434C5E';
            });

            sideTab.addEventListener('mouseleave', () => {
                sideTab.style.width = '40px';
                sideTab.style.backgroundColor = '#3B4252';
            });

            // ���������� ����� �� ������
            sideTab.onclick = () => {
                modal.style.display = 'flex';
            };

            document.body.appendChild(sideTab);

            // ������ ��������� ����
            const modal = document.createElement('div');
            modal.style.position = 'fixed';
            modal.style.top = '0';
            modal.style.left = '0';
            modal.style.width = '100%';
            modal.style.height = '100%';
            modal.style.backgroundColor = 'rgba(0,0,0,0.7)';
            modal.style.display = 'none';
            modal.style.justifyContent = 'center';
            modal.style.alignItems = 'center';
            modal.style.zIndex = '1001';
            modal.style.animation = 'fadeIn 0.3s ease';

            const modalContent = document.createElement('div');
            modalContent.style.backgroundColor = '#2E3440';
            modalContent.style.padding = '25px';
            modalContent.style.borderRadius = '12px';
            modalContent.style.minWidth = '320px';
            modalContent.style.maxWidth = '90%';
            modalContent.style.maxHeight = '90vh';
            modalContent.style.overflowY = 'auto';
            modalContent.style.boxShadow = '0 8px 16px rgba(0, 0, 0, 0.4)';
            modalContent.style.fontFamily = 'Arial, sans-serif';
            modalContent.style.color = '#D8DEE9';
            modalContent.style.animation = 'slideIn 0.3s ease';

            modalContent.innerHTML = `
    <h2 style=""margin-top: 0; color: #88C0D0; text-align: center;"">���������</h2>
    
    <div style=""margin-bottom: 20px;"">
        <label for=""comboBox"" style=""display: block; margin-bottom: 5px; font-weight: bold;"">��� ����:</label>
        <select id=""comboBox"" style=""
            width: 100%; padding: 10px; 
            background: #3B4252; 
            color: #D8DEE9; 
            border: 1px solid #4C566A; 
            border-radius: 6px; 
            outline: none;"">
            <option value=""fire"" ${settings.ballValue === 'fire' ? 'selected' : ''}>?? ��������</option>
            <option value=""explosive"" ${settings.ballValue === 'explosive' ? 'selected' : ''}>?? ���������</option>
        </select>
    </div>
  
    <div style=""margin-bottom: 20px;"">
        <label style=""font-weight: bold;"">
            <input type=""checkbox"" id=""greenLamp"" ${settings.greenLamp ? 'checked' : ''} style=""margin-right: 8px;""> 
            ������������ ������ ����
        </label>
    </div>
  
    <div style=""display: flex; flex-direction: column; align-items: center; gap: 20px; margin-bottom: 20px;"">
        <div style=""width: 100%; max-width: 320px;"">
            <label for=""pauseStart"" style=""display: block; margin-bottom: 5px; font-weight: bold; text-align: center;"">
                ����� ������ �����:
            </label>
            <input type=""time"" id=""pauseStart"" 
                value=""${settings.pauseStart}"" 
                style=""width: 100%; padding: 10px; background: #3B4252; color: #D8DEE9; border: 1px solid #4C566A; border-radius: 6px; outline: none; box-sizing: border-box;"">
        </div>
  
        <div style=""width: 100%; max-width: 320px;"">
            <label for=""pauseEnd"" style=""display: block; margin-bottom: 5px; font-weight: bold; text-align: center;"">
                ����� ��������� �����:
            </label>
            <input type=""time"" id=""pauseEnd"" 
                value=""${settings.pauseEnd}"" 
                style=""width: 100%; padding: 10px; background: #3B4252; color: #D8DEE9; border: 1px solid #4C566A; border-radius: 6px; outline: none; box-sizing: border-box;"">
        </div>
  
        <div style=""width: 100%; max-width: 320px;"">
            <label style=""font-weight: bold;"">
                <input type=""checkbox"" id=""autoPause"" ${settings.autoPause ? 'checked' : ''} style=""margin-right: 8px;"">
                ������������� �������� � ��������� ����� �� ����������
            </label>
        </div>
    </div>
  
    <div style=""display: flex; justify-content: space-between; gap: 10px;"">
        <button id=""saveSettings"" style=""
            padding: 10px 20px; 
            background-color: #5E81AC; 
            color: white; 
            border: none; 
            border-radius: 6px; 
            cursor: pointer;
            transition: background-color 0.3s;"">
            ?? ���������
        </button>
        <button id=""closeModal"" style=""
            padding: 10px 20px; 
            background-color: #BF616A; 
            color: white; 
            border: none; 
            border-radius: 6px; 
            cursor: pointer;
            transition: background-color 0.3s;"">
            ? �������
        </button>
    </div>
`;

            modal.appendChild(modalContent);
            document.body.appendChild(modal);

            // ��������� CSS ��������
            const style = document.createElement('style');
            style.textContent = `
    @keyframes fadeIn {
        from { opacity: 0; }
        to { opacity: 1; }
    }
    
    @keyframes slideIn {
        from {
            transform: translateY(-20px);
            opacity: 0;
        }
        to {
            transform: translateY(0);
            opacity: 1;
        }
    }
`;
            document.head.appendChild(style);

            // ������� ��������� ��� ������
            modalContent.querySelector('#saveSettings').addEventListener('mouseenter', (e) => {
                e.target.style.backgroundColor = '#81A1C1';
            });
            modalContent.querySelector('#saveSettings').addEventListener('mouseleave', (e) => {
                e.target.style.backgroundColor = '#5E81AC';
            });

            modalContent.querySelector('#closeModal').addEventListener('mouseenter', (e) => {
                e.target.style.backgroundColor = '#D08770';
            });
            modalContent.querySelector('#closeModal').addEventListener('mouseleave', (e) => {
                e.target.style.backgroundColor = '#BF616A';
            });

            // ����������� �������
            modalContent.querySelector('#saveSettings').onclick = () => {
                settings.greenLamp = document.getElementById('greenLamp').checked;
                settings.ballValue = document.getElementById('comboBox').value;
                settings.pauseStart = document.getElementById('pauseStart').value;
                settings.pauseEnd = document.getElementById('pauseEnd').value;
                settings.autoPause = document.getElementById('autoPause').checked;

                localStorage.setItem('greenLamp', settings.greenLamp);
                localStorage.setItem('ballValue', settings.ballValue);
                localStorage.setItem('pauseStart', settings.pauseStart);
                localStorage.setItem('pauseEnd', settings.pauseEnd);
                localStorage.setItem('autoPause', settings.autoPause);

                modal.style.display = 'none';
                console.log('log', { message: `Settings saved ${JSON.stringify(settings)}` });
            };

            // �������� �� ����� �� ���
            modal.onclick = (e) => {
                if (e.target === modal) {
                    modal.style.display = 'none';
                }
            };

            // �������� �� Escape
            document.addEventListener('keydown', (e) => {
                if (e.key === 'Escape' && modal.style.display === 'flex') {
                    modal.style.display = 'none';
                }
            });

            const pauseStartTime = modalContent.querySelector('#pauseStart');
            pauseStartTime.addEventListener('change', () => {
                pauseStart = pauseStartTime.value;
                localStorage.setItem('pauseStart', pauseStart);
                console.log('log', { message: `pauseStartTime is now ${pauseStart}` });
            });

            const pauseEndTime = modalContent.querySelector('#pauseEnd');
            pauseEndTime.addEventListener('change', () => {
                pauseEnd = pauseEndTime.value;
                localStorage.setItem('pauseEnd', pauseEnd);
                console.log('log', { message: `pauseEndTime is now ${pauseEnd}` });
            });

            const greenLampCheckbox = modalContent.querySelector('#greenLamp');
            greenLampCheckbox.addEventListener('change', () => {
                greenLamp = greenLampCheckbox.checked;
                localStorage.setItem('greenLamp', greenLamp);
                console.log('log', { message: `greenLamp is now ${greenLamp}` });
            });

            const autoPauseCheckbox = modalContent.querySelector('#autoPause');
            autoPauseCheckbox.addEventListener('change', () => {
                autoPause = autoPauseCheckbox.checked;
                localStorage.setItem('autoPause', autoPause);
                console.log('log', { message: `autoPause is now ${autoPause}` });
            });

            const comboBox = modalContent.querySelector('#comboBox');
            comboBox.addEventListener('change', () => {
                ballValue = comboBox.value;
                localStorage.setItem('ballValue', ballValue);
                console.log('log', { message: `ballValue is now ${ballValue}` });
            });

            modalContent.querySelectorAll('button').forEach(button => {
                button.addEventListener('mouseenter', () => {
                    button.style.opacity = '0.8';
                });
                button.addEventListener('mouseleave', () => {
                    button.style.opacity = '1';
                });
            });

            comboBox.addEventListener('focus', () => {
                comboBox.style.borderColor = '#2196F3';
            });

            comboBox.addEventListener('blur', () => {
                comboBox.style.borderColor = '#ccc';
            });

            modalContent.querySelector('#closeModal').onclick = () => {
                modal.style.display = 'none';
            };

            document.addEventListener('keydown', function (event) {
                if (event.ctrlKey && event.altKey && event.key === 'p') {
                    isPaused = !isPaused;
                    controlButton.innerText = isPaused ? '??' : '??';
                    console.log(isPaused ? 'Script paused via Ctrl+P' : 'Script resumed via Ctrl+P');
                    console.log('log', { message: isPaused ? 'Script paused via Ctrl+P' : 'Script resumed via Ctrl+P' });
                }
            });

            async function checkPauseTime() {
                const now = new Date();

                // �������� ������� ����� �� ���
                const mskTime = new Date(now.toLocaleString(""en-US"", { timeZone: ""Europe/Moscow"" }));
                const currentTime = `${mskTime.getHours().toString().padStart(2, '0')}:${mskTime.getMinutes().toString().padStart(2, '0')}`;

                const pauseStart = localStorage.getItem(""pauseStart"") || ""00:00"";
                const pauseEnd = localStorage.getItem(""pauseEnd"") || ""00:00"";
                const autoPause = localStorage.getItem(""autoPause"") === ""true"";

                console.log(`[${currentTime}] �������� �����: ${pauseStart} - ${pauseEnd}, ����: ${autoPause}`);

                if (!autoPause) return; // ���� ������ �������� - ������ �� ������

                if (currentTime === pauseStart) {
                    console.log(""?? ������ ����������!"");
                    stopScript();
                } else if (currentTime === pauseEnd) {
                    console.log(""? ������ �������!"");
                    await startScript();
                }
            }

            function stopScript() {
                console.log(""������� ���������� ������� ���������."");
                socket.send(`JOIN F\r\n`);
            }

            async function startScript() {
                console.log(""������� ��������� ������� ���������."");
                await nextRandomCannonPlanet();
            }

            setInterval(checkPauseTime, 60000);

            async function adv(input) {
                let result = getTextAfterSecondColon(input);
                let parts = getTextAfterSecondColonArray(input);

                console.log('log', { message: result });

                if (parts[0].toLowerCase() !== bot.nick.toLowerCase() && result.toLowerCase().includes(""������ ������"")) {
                    // ��������� � �������
                    executionQueue.push(1);

                    console.log('log', { message: `[${executionQueue.length}] ��� ���������� ������ ������!` });

                    if (executionQueue.length >= 5) {
                        console.log('log', { message: `[${executionQueue.length}] ��� ������ ���������� ������ ������!` });

                        socket.send(""T 0 1\r\n"");
                        await nextRandomCannonPlanet();

                        // �������� ������� � ������
                        executionQueue = [];
                        tracker.clearHits(""GreenLight"");
                    }

                    if (tracker.trackCodeBlock(""GreenLight"")) {
                        console.log('log', { message: `�� ��������� ������, [${tracker.countHits(""GreenLight"")}] ��� ���������� � ������ ������!` });

                        socket.send(""T 0 1\r\n"");
                        await nextRandomCannonPlanet();

                        // �������� ������� � ������
                        executionQueue = [];
                        tracker.clearHits(""GreenLight"");
                    }
                } else {
                    executionQueue = [];
                }
            }

            async function nextRandomCannonPlanet() {
                // �������� ���������� � User-Agent, ������ ���������� � ������ ����������
                const userAgent = navigator.userAgent;
                const platform = navigator.platform;
                const orientation = window.innerWidth > window.innerHeight ? ""landscape"" : ""portrait"";
                const width = window.innerWidth;
                const height = window.innerHeight;
                const dpi = window.devicePixelRatio;

                // ���������� ��������� ������ ����������
                const webUserAgentModel = platform; // ���������� platform ��� ��������� (����� ��������, ��������, ������� userAgent)

                try {
                    const headers = {
                        ""x-galaxy-client-ver"": ""9.5"",
                        ""x-galaxy-kbv"": ""352"",
                        ""x-galaxy-lng"": ""ru"",
                        ""x-galaxy-model"": webUserAgentModel,
                        ""x-galaxy-orientation"": orientation,
                        ""x-galaxy-os-ver"": ""1"",
                        ""x-galaxy-platform"": ""web"",
                        ""x-galaxy-scr-dpi"": `${dpi}`,
                        ""x-galaxy-scr-h"": `${height}`,
                        ""x-galaxy-scr-w"": `${width}`,
                        ""x-galaxy-user-agent"": userAgent
                    };

                    const response = await fetch(`https://galaxy.mobstudio.ru/services/?a=game_planets&userID=${bot.id.trim()}&password=${bot.pass.trim()}&usercur=${bot.id.trim()}&random=${Math.random()}`, {
                        method: 'GET',
                        headers: headers
                    });

                    if (response.ok) {
                        const result = await response.text();

                        const randomPlanet = getRandomCannonPlanetName(result, bot.currentPlanet);

                        console.log(`������� �������� �������, � ���������� ������������ ����� [${randomPlanet.name}] [${randomPlanet.count}]`);

                        socket.send(`JOIN ${randomPlanet.name}\r\n`);

                        console.log(`���������� �� ������� [${randomPlanet.name}]`);
                    }
                } catch (ex) {
                    console.error(`������ ��� ���������� �������: ${ex.message}`);
                }
            }

            function getRandomCannonPlanetName(input, currentPlanet) {
                const parser = new DOMParser();
                const doc = parser.parseFromString(input, 'text/html');

                // ���� ��� �������� � ������� 'bsm-plank-text', ���������� ���������� � ��������
                const planetElements = doc.querySelectorAll('.bsm-plank-text');

                // ������ ��� �������� ��������� ������
                let planets = [];

                // ��������� ������ � ������ �������
                planetElements.forEach(element => {
                    const planetText = element.textContent.trim();  // ����� ���� ""Cannon*12 [35]""
                    console.log('����� �������:', planetText);  // ������� ����� ��� �������

                    const regex = /Cannon\*(\d+)\s\[(\d+)\]/;  // ���������� ��������� ��� ���������� ������

                    const match = planetText.match(regex);
                    if (match) {
                        const name = `Cannon*${match[1]}`;  // �������� �������
                        const count = parseInt(match[2]);  // ���������� ����� �� �������

                        console.log('���������:', name, count);  // ������� ����� ��� �������

                        // ��������� ������� � ������, ���� ��� �� �������� �������
                        if (name !== currentPlanet) {
                            planets.push({ name, count });
                        }
                    }
                });

                // ���������, ��� ������ �� ����
                if (planets.length === 0) {
                    throw new Error('No planet names found.');
                }

                // ���������� �� ���������� ����� �� ������� (�� ��������)
                planets.sort((a, b) => b.count - a.count);

                // �������� 10 ������ � ���������� ����������
                const mostPopulatedPlanets = planets.slice(0, 10);

                // ��������� ����� ����� ������� �� ������
                const randomIndex = Math.floor(Math.random() * mostPopulatedPlanets.length);

                const selectedPlanet = mostPopulatedPlanets[randomIndex];
                console.log('������� �������:', selectedPlanet);  // ������� ����� ��� �������

                return selectedPlanet;
            }

            async function act(input, info) {
                console.log('log', { message: info });

                if (isBotShooting(input, info)) {
                    const randomDelay = randomNext(2000, 3000);
                    const seconds = (randomDelay / 1000).toFixed(1);

                    console.log('log', { message: `������� ����� ����� [${seconds}] ������!` });

                    await delay(randomDelay);

                    loadBall();
                }
            }

            function isBotShooting(input, info) {
                return (
                    input.includes(bot.id) &&
                    info.toLowerCase().includes(`${bot.nick.toLowerCase()} �������� �� �����`)
                );
            }

            function srv(input, str) {
                if (isPaused) return;

                if (/������ �������� ""��� ������"", ����� ������ �������/i.test(input)) {
                    console.log('log', { message: ""������������� �������� ��������� ���� � �����."" });
                    loadBall();
                }
                else if (/���� ����� ��������/i.test(input) || /�� � ����. ������ �������� �����, ����� �������� ������/i.test(input)) {
                    isLoadBall = true;
                    console.log('log', { message: `���� ����� �������� �����` });
                }
                else if ((/������� ���� � �����/i.test(input) || /������� � ����/i.test(input)) && !isLoadBall) {
                    isLoadBall = false;
                    console.log('log', { message: ""������� ���� � �����"" });
                    loadBall();
                }

                const match = str.match(/��� (.+?) ����/);
                if (match) {
                    isLoadBall = false;
                    console.log('log', { message: `� ���� ��� ${match[1]} ����!` });
                    socket.send(`QUIT :ds\r\n`);
                }
            }

            async function viewScript(input, parts) {
                if (!gameRun || !isLoadBall) {
                    return;
                }

                if (gameRun && isLoadBall) {
                    if (parts[2].includes(gameBotId.toString())) {
                        if (isPaused) return;
                        viewsScript(input);
                    }

                    await searchObj();

                    if (myWeapons !== nextWeapons) {
                        await weaponControl();
                    }

                    if (myPosition !== nextPosition) {
                        await controlPosition();
                    }
                }
            }

            function viewsScript(data) {
                try {
                    const tokens = data.trim().split(/\s+/);
                    if (tokens.length < 3) return;

                    const aW = tokens[1];
                    const array = tokens[2].split(',');
                    const rawData = tokens.slice(3).join(' ');

                    if (!rawData.includes('{') || !rawData.includes('}')) return;

                    const ao = rawData.substring(0, rawData.indexOf('}'));
                    const a3 = rawData.substring(rawData.indexOf('{') + 1, rawData.lastIndexOf('}'));

                    array.forEach(() => addViewScript(aW, ao, a3));
                } catch (e) {
                    console.error(e.message);
                }
            }

            function addViewScript(a, b, c) {
                const array2 = b.trim().split(/\s+/);
                const aw = c.toLowerCase();
                const m = aw.includes('(') ? aw.substring(aw.indexOf('(') + 1, aw.indexOf(')')) : null;
                const Mn = m ? m.split(',') : [];

                array2.forEach(val => {
                    const cc = val.split(';');
                    if (cc.length < 5) return;

                    Mn.forEach(mnItem => {
                        if (cc[0] === mnItem) {
                            processView(cc);
                        }
                    });
                });
            }

            function processView(cc) {
                const cc2 = cc.slice(4).join(';').split('cn/').slice(1);

                if (cc[0] === '300') return;

                cc2.forEach(item => {
                    const ob = item.split(';');
                    if (ob.length < 4) return;

                    const [id, i1, i2, i3] = ob;

                    if (!['f', 'b', 'h', 'exp'].includes(id)) {
                        const intI1 = parseInt(i1, 10);
                        const intI2 = parseInt(i2, 10);

                        if (isNaN(intI1) || isNaN(intI2)) return;

                        if (intI2 > 44 || intI2 < 0) {
                            views.delete(cc[0]);
                        } else {
                            views.set(cc[0], new ViewsObject(cc[0], id, i1, i2, i3));
                        }
                    }
                });
            }

            async function addView(input, parts) {
                try {
                    if (parts[2].includes(bot.id) && parts[3].includes(""95"")) {
                        const strArray3 = parts[4].split(';');
                        const num1 = parseInt(strArray3[2], 10);
                        const num2 = Math.abs(num1 - 9 - 300);
                        myPosition = num2;
                        console.log('log', { message: `������� ������ ����: [${num2}]` });
                    }

                    if (parts[2].includes(gameBotId) || parts[2].includes(bot.id)) {
                        if (isPaused) return;
                        await processAddView(input);
                    }
                } catch (e) {
                    console.log('log', { message: `������ � addView: ${e.message}` });
                }
            }

            async function processAddView(input) {
                try {
                    const ls = input.split(/\s+/);

                    if (ls[2] === bot.id) {
                        if (ls.length > 0) {

                            const sw = ls[4].split("";"")[1];

                            switch (sw) {
                                case ""cn/s"":
                                    console.log('log', { message: ""���� �����: ������� [׸����]"" });
                                    break;
                                case ""cn/gcn"":
                                    console.log('log', { message: ""���� �����: ���������� [�������]"" });
                                    break;
                                default:
                                    const positionMap = {
                                        ""cn/c1"": 1, ""cn/c2"": 2, ""cn/c3"": 3, ""cn/c4"": 4, ""cn/c5"": 5,
                                        ""cn/gcn1"": 1, ""cn/gcn2"": 2, ""cn/gcn3"": 3, ""cn/gcn4"": 4, ""cn/gcn5"": 5
                                    };
                                    if (positionMap[sw]) {
                                        myWeapons = positionMap[sw];
                                        console.log('log', { message: `������� �����: [${myWeapons}]` });
                                    }
                                    break;
                            }
                        }
                    }

                    if (/light_r/i.test(input)) {
                        light = false;
                        console.log('log', { message: ""��������� ������� ����!"" });
                    } else if (/light_g/i.test(input)) {
                        light = true;
                        console.log('log', { message: ""��������� ������ ����!"" });

                        if (action && light) {
                            if (greenLamp) {
                                socket.send(`ACTION 6770 ${gameBotId}\r\n`);
                                console.log('log', { message: ""�������� � ������ ������!"" });
                            }
                            else {
                                const randomDelay = randomNext(2000, 5000);
                                const seconds = (randomDelay / 1000).toFixed(1);
                                console.log('log', { message: `��������� ����� [${seconds}] ������!` });

                                await delay(randomDelay);

                                if (action && light) {
                                    socket.send(`ACTION 2401 ${gameBotId}\r\n`);
                                } else {
                                    console.log('log', { message: ""�������� ������� �����, ���������� ������ ������!"" });
                                }
                            }
                        } else {
                            console.log('log', { message: ""�������� ������� �����!"" });
                        }
                    }

                } catch (e) {
                    console.log('log', { message: `������ � processAddView: ${e.message}` });
                }
            }

            async function searchObj() {
                try {
                    action = false;

                    for (const view of views.values()) {
                        if (view.id === ""6000"") {
                            const i1 = parseInt(view.i1, 10);
                            const i2 = parseInt(view.i2, 10);
                            const i3 = parseInt(view.i3, 10);

                            if (!isNaN(i1) && !isNaN(i2) && !isNaN(i3)) {
                                if (i1 === -50 && i2 === 0 && i3 === 33) {
                                    const randomPosition = randomNext(363, 368);
                                    prizePosition = 8;
                                    nextPosition = randomPosition;
                                    nextWeapons = 3;
                                    action = true;
                                    break;
                                } else if (i1 === -25 && i2 === 0 && i3 === 33) {
                                    const randomPosition = randomNext(378, 388);
                                    prizePosition = 9;
                                    nextPosition = randomPosition;
                                    nextWeapons = 3;
                                    action = true;
                                    break;
                                } else if (i1 === 0 && i2 === 0 && i3 === 33) {
                                    const randomPosition = randomNext(390, 399);
                                    prizePosition = 10;
                                    nextPosition = randomPosition;
                                    nextWeapons = 3;
                                    action = true;
                                    break;
                                }

                                if (action) break;
                            }
                        }
                    }
                } catch (error) {
                    console.log('log', { message: `������ � searchObj: ${error.message}` });
                }

                return Promise.resolve();
            }

            async function weaponControl() {
                if (myWeapons !== nextWeapons) {
                    if (myWeapons === nextWeapons) {
                        console.log('log', { message: ""������� ����� ��� �� ������ �������. ��� ������������."" });
                        return;
                    }

                    if (nextWeapons < 1 || nextWeapons > 5) {
                        console.log('log', { message: ""�������� ������� ����� ������� �� ������� ���������� �������� (1-5). ��� ������������."" });
                        return;
                    }

                    const randomDelay = randomNext(4000, 5000);
                    const seconds = (randomDelay / 1000).toFixed(1);
                    console.log('log', { message: `������ ������� ����� ����� [${seconds}] ������!` });

                    await delay(randomDelay);

                    if (myWeapons < nextWeapons) {
                        weaponsUp();
                    } else {
                        weaponsDown();
                    }
                }
            }

            async function controlPosition() {
                const isWithinRange = isWithinPrizeRange(prizePosition, myPosition);
                const randomDelay = randomNext(1500, 3000);

                if (!isWithinRange && myPosition !== nextPosition) {
                    myPosition = nextPosition;

                    const seconds = (randomDelay / 1000).toFixed(1);
                    console.log('log', { message: `������ ������� ���� ����� [${seconds}] ������!` });

                    await delay(randomDelay);

                    socket.send(`REMOVE ${nextPosition}\r\n`);
                }
            }

            function remove(parts) {
                if (parts[1].includes(bot.id)) {
                    myPosition = parseInt(parts[2]);
                    console.log('log', { message: `������� ������� ������ ����: ${parts[2]}` });
                }
            }

            function isWithinPrizeRange(prizePosition, myPosition) {
                switch (prizePosition) {
                    case 8:
                        return myPosition >= 363 && myPosition <= 368;
                    case 9:
                        return myPosition >= 378 && myPosition <= 388;
                    case 10:
                        return myPosition >= 390 && myPosition <= 399;
                    default:
                        return false;
                }
            }

            function weaponsDown() {
                const num = myWeapons - nextWeapons;
                for (let index = 0; index < num; index++) {
                    socket.send(`ACTION 2403 ${gameBotId}\r\n`);
                }

                myWeapons = nextWeapons;
                console.log('log', { message: `�������� ����� �� [${nextWeapons}] �������!` });
            }

            function weaponsUp() {
                const num = nextWeapons - myWeapons;
                for (let index = 0; index < num; index++) {
                    socket.send(`ACTION 2402 ${gameBotId}\r\n`);
                }

                myWeapons = nextWeapons;
                console.log('log', { message: `������� ����� �� [${nextWeapons}] �������!` });
            }

            function loadBall() {
                if (!ballValue) {
                    console.log(""log"", { message: ""����� ���� �� ������! ������� 'f' ��� 'e' ��� ������."" });
                    return;
                }

                const trimmedValue = ballValue.trim();
                switch (trimmedValue) {
                    case ""explosive"":
                        console.log(""log"", { message: ""�������� ��������� ����!"" });
                        socket.send(`ACTION 8910 ${gameBotId}\r\n`);
                        break;
                    case ""fire"":
                        console.log(""log"", { message: ""�������� �������� ����!"" });
                        socket.send(`ACTION 4493 ${gameBotId}\r\n`);
                        break;
                    default:
                        console.log(""log"", { message: `����������� ����� ����: ${trimmedValue}` });
                        break;
                }
            }

            function delay(ms) {
                return new Promise((resolve) => setTimeout(resolve, ms));
            }

            function randomNext(min, max) {
                return Math.floor(Math.random() * (max - min + 1)) + min;
            }

            function parser353(str, join = false) {
                try {
                    const CHARACTER_PARAMS_PER_SUIT = 5;
                    const tokens = str.trim().split(/\s+/);

                    let i = 0;
                    while (i < tokens.length) {
                        let clan = tokens[i];
                        let nick = tokens[i + 1];
                        const id = tokens[i + 2];
                        const K = Math.abs(parseInt(tokens[i + 3], 10));
                        const position = tokens[i + 4 + K * CHARACTER_PARAMS_PER_SUIT];

                        let stars = false;
                        let owner = false;

                        if (nick.startsWith('+')) {
                            nick = nick.substring(1).trim();
                            stars = true;
                        }

                        if (nick.startsWith('@')) {
                            nick = nick.substring(1).trim();
                            owner = true;
                        }

                        i += 5 + K * CHARACTER_PARAMS_PER_SUIT;

                        const userData = {
                            id: parseInt(id, 10),
                            nick,
                            clan,
                            position: parseInt(position, 10),
                            owner,
                            stars,
                            join
                        };

                        users.set(userData.id, new User(userData));

                        console.log('data', userData);

                        if (join) break;
                    }
                } catch (e) {
                    console.error(`parser 353 error: ${e.message}`);
                }
            }

            function handle900(parts) {
                bot.currentPlanet = parts[1];
                console.log('log', { message: `������� ��������������: ${bot.nick} [${parts[1]}]` });

                if (parts[1].toLowerCase().includes(""cannon*"")) {
                    gameRun = true;

                    const nickname = parts[1].trim();
                    const user = searchNickUser(nickname);

                    if (user) {
                        gameBotId = user.id;
                        console.log('log', { message: `������ ������������: ${user.nick}, ID: ${user.id}` });
                    } else {
                        console.log('log', { message: `������������ � ����� ""${nickname}"" �� ������.` });
                    }
                }
                else {
                    gameRun = false;
                }
            }

            function userJoin(input, nick, id) {
                if (bot.id !== id.toString()) {
                    console.log('log', { message: `�� ������� ������� �������� => ${nick}` });
                }

                const userJoin = getUser(id);
                if (!userJoin) {
                    const joinData = input.trim().substring(input.indexOf("" "") + 1);
                    parser353(joinData, true);
                }
            }

            function searchNickUser(nick) {
                for (const user of users.values()) {
                    if (user.nick.toLowerCase().includes(nick.toLowerCase())) {
                        return user;
                    }
                }
                return null;
            }

            function getUser(id) {
                return users.get(id) || null;
            }

            function removeUser(id) {
                users.delete(id);
            }

            function userExit(id) {
                const exitingUser = getUser(id);
                if (exitingUser) {
                    console.log('log', { message: `������� ������� => ${exitingUser.nick}` });
                }
                removeUser(id);
            }

            function getTextAfterSecondColon(input) {
                const firstColonIndex = input.indexOf("":"");
                if (firstColonIndex === -1) return """";

                const secondColonIndex = input.indexOf("":"", firstColonIndex + 1);
                if (secondColonIndex === -1) return """";

                return input.substring(secondColonIndex + 1).trim();
            }

            function getTextAfterSecondColonArray(input) {
                const firstColonIndex = input.indexOf("":"");
                if (firstColonIndex === -1) return [];

                const secondColonIndex = input.indexOf("":"", firstColonIndex + 1);
                if (secondColonIndex === -1) return [];

                const textAfterSecondColon = input.substring(secondColonIndex + 1).trim();

                const separators = /[ , \n\r]+/;
                const result = textAfterSecondColon.split(separators).filter(Boolean);

                return result;
            }

            function closeDialogs() {
                // �������� �������� � ���������� � 1 �������
                for (let i = 1; i <= 5; i++) {
                    setTimeout(() => {
                        const closeButton = document.querySelector("".dialog__close-button"");
                        if (closeButton) closeButton.click();
                    }, i * 1000);
                }
            }

            return socket;
        };
    } catch (error) {
        console.log(""ws_error"", { error: error.message });
    }
})();";

                        // 2. ������������� ������
                        var obfuscatedResult = Uglify.Js(script);
                        var obfuscatedScript = obfuscatedResult.Code;

                        if (obfuscatedResult.HasErrors)
                        {
                            _logger.LogError("������ ���������� �������.");
                            // � ���� ������ ����� �������� ������������ ������ ��� ������� ������
                        }

                        // 3. �������� ��������������� ������
                        var scriptNode = doc.CreateElement("script");
                        scriptNode.InnerHtml = obfuscatedScript;
                        body.AppendChild(scriptNode);

                        // --- ����� ��������� ---
                    }

                    var modifiedHtml = doc.DocumentNode.OuterHtml;
                    return Content(modifiedHtml, "text/html; charset=utf-8", Encoding.UTF8);
                }
                else
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    return new FileContentResult(content, contentType ?? "application/octet-stream");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "������ ��� ������������� ������� �� {Url}", targetUrl);
                return StatusCode(500, "���������� ������ ������� ��� ������������� �������.");
            }
        }
    }
}