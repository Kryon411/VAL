using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class DockModelService : IDockModelService
    {
        private readonly IWebMessageSender _webMessageSender;
        private readonly IPrivacySettingsService _privacySettingsService;
        private readonly object _sync = new();

        private bool _continuumLoggingEnabled = true;
        private bool _portalCaptureEnabled = true;
        private bool _portalEnabled;
        private bool _portalPrivacyAllowed = true;
        private int _portalCount;

        public DockModelService(IWebMessageSender webMessageSender, IPrivacySettingsService privacySettingsService)
        {
            _webMessageSender = webMessageSender;
            _privacySettingsService = privacySettingsService;

            ApplyPrivacySnapshot(_privacySettingsService.GetSnapshot());
            _privacySettingsService.SettingsChanged += OnPrivacySettingsChanged;
        }

        public void Publish(string? chatId = null)
        {
            DockModel model;
            string? resolvedChatId = chatId;
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(resolvedChatId))
                    resolvedChatId = SessionContext.ActiveChatId;

                model = BuildModel(resolvedChatId);
            }

            try
            {
                var payload = JsonSerializer.SerializeToElement(model);
                _webMessageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = WebCommandNames.DockModel,
                    ChatId = resolvedChatId,
                    Source = "host",
                    Payload = payload,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            catch
            {
                ValLog.Warn(nameof(DockModelService), "Failed to publish dock model.");
            }
        }

        public void UpdatePortalState(bool enabled, bool privacyAllowed, int count)
        {
            lock (_sync)
            {
                _portalEnabled = enabled;
                _portalPrivacyAllowed = privacyAllowed;
                _portalCount = Math.Max(0, Math.Min(10, count));
            }

            Publish();
        }

        private void OnPrivacySettingsChanged(PrivacySettingsSnapshot snapshot)
        {
            ApplyPrivacySnapshot(snapshot);
            Publish();
        }

        private void ApplyPrivacySnapshot(PrivacySettingsSnapshot snapshot)
        {
            lock (_sync)
            {
                _continuumLoggingEnabled = snapshot.ContinuumLoggingEnabled;
                _portalCaptureEnabled = snapshot.PortalCaptureEnabled;
                _portalPrivacyAllowed = snapshot.PortalCaptureEnabled;
            }
        }

        private DockModel BuildModel(string? chatId)
        {
            var portalActive = _portalEnabled || _portalCount > 0;
            var portalLabel = _portalEnabled ? "Portal Armed" : "Portal";
            var portalSubtitle = _portalPrivacyAllowed ? "Hotkeys enabled" : "Disabled by Privacy setting";
            var portalSendHint = _portalCount <= 0
                ? "Stage at least one capture to enable Send."
                : "Send will paste all staged captures into the composer.";

            var portalToggleDisabled = !_portalPrivacyAllowed;
            var portalToggleReason = portalToggleDisabled ? "Disabled by Privacy setting." : null;

            var portalSendDisabled = !_portalPrivacyAllowed || !_portalEnabled || _portalCount <= 0;
            string? portalSendReason = null;
            if (!_portalPrivacyAllowed)
                portalSendReason = "Portal capture is disabled in Privacy settings.";
            else if (!_portalEnabled)
                portalSendReason = "Arm Capture & Stage to enable Send.";
            else if (_portalCount <= 0)
                portalSendReason = "Stage at least one capture to enable Send.";

            var continuumToggle = new DockItem
            {
                Id = "continuumLogging",
                Type = "toggle",
                Label = "Continuum logging",
                State = _continuumLoggingEnabled,
                Tooltip = "Allow Continuum to write Truth.log for this session.",
                Command = new DockCommand
                {
                    Name = WebCommandNames.PrivacyCommandSetContinuumLogging
                }
            };

            var sections = new List<DockSection>
            {
                new DockSection
                {
                    Id = "continuum",
                    Title = "Continuum",
                    HeaderControl = continuumToggle,
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-grid",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "pulse",
                                    Type = "button",
                                    Label = "Pulse",
                                    Kind = "primary",
                                    Tooltip = "Open a new chat with a summary of your current conversation and guidelines for a smooth transition.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ContinuumCommandPulse,
                                        RequiresChatId = true
                                    }
                                },
                                new DockItem
                                {
                                    Id = "prelude",
                                    Type = "button",
                                    Label = "Prelude",
                                    Kind = "secondary",
                                    Tooltip = "Add the session setup and instructions to the current chat.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ContinuumCommandInjectPreamble,
                                        RequiresChatId = true
                                    }
                                },
                                new DockItem
                                {
                                    Id = "chronicle",
                                    Type = "button",
                                    Label = "Chronicle",
                                    Kind = "ghost",
                                    Tooltip = "Scan the current chat and rebuild VAL’s memory for this session.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ContinuumCommandChronicleRebuildTruth,
                                        RequiresChatId = true
                                    }
                                },
                                new DockItem
                                {
                                    Id = "sessionFiles",
                                    Type = "button",
                                    Label = "Session Files",
                                    Kind = "ghost",
                                    Tooltip = "Open the folder on your computer where this session’s files are stored.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ContinuumCommandOpenSessionFolder,
                                        RequiresChatId = true
                                    }
                                }
                            }
                        },
                        new DockBlock
                        {
                            Id = "continuumHint",
                            Type = "hint",
                            ClassName = "valdock-section-hint",
                            Text = "Session tools for jumps and memory."
                        },
                        new DockBlock
                        {
                            Id = "pulseStatusHint",
                            Type = "hint",
                            ClassName = "valdock-section-hint valdock-status-hint is-muted",
                            Text = "Pulse opens a fresh chat with a summarized handoff."
                        }
                    }
                },
                new DockSection
                {
                    Id = "portal",
                    Title = "Portal",
                    Subtitle = portalSubtitle,
                    HeaderControl = new DockItem
                    {
                        Id = "portalPrivacy",
                        Type = "toggle",
                        Label = "Portal capture & hotkeys",
                        State = _portalCaptureEnabled,
                        Tooltip = "Allow Portal to register hotkeys and monitor the clipboard.",
                        Command = new DockCommand
                        {
                            Name = WebCommandNames.PrivacyCommandSetPortalCapture
                        }
                    },
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-inline-row",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "portalToggle",
                                    Type = "toggle",
                                    Label = "Capture & Stage",
                                    State = _portalEnabled,
                                    Tooltip = "Arm Portal. Press 1 to open Screen Snip. Any clipboard images will stage (max 10).",
                                    Disabled = portalToggleDisabled,
                                    DisabledReason = portalToggleReason,
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.PortalCommandSetEnabled
                                    }
                                },
                                new DockItem
                                {
                                    Id = "portalCount",
                                    Type = "count",
                                    Count = _portalCount,
                                    Max = 10
                                },
                                new DockItem
                                {
                                    Id = "portalSend",
                                    Type = "button",
                                    Label = "Send",
                                    Kind = "secondary",
                                    Tooltip = "Paste all staged clipboard images into the composer (max 10).",
                                    Disabled = portalSendDisabled,
                                    DisabledReason = portalSendReason,
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.PortalCommandSendStaged,
                                        Payload = JsonSerializer.SerializeToElement(new { max = 10 })
                                    }
                                }
                            }
                        },
                        new DockBlock
                        {
                            Id = "portalSendHint",
                            Type = "hint",
                            ClassName = "valdock-section-hint valdock-status-hint",
                            Text = portalSendHint
                        }
                    }
                },
                new DockSection
                {
                    Id = "abyss",
                    Title = "Abyss",
                    Subtitle = "Recall & search",
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-row valdock-actions",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "abyssSearch",
                                    Type = "button",
                                    Label = "Search",
                                    Kind = "primary",
                                    Tooltip = "Open Abyss search and enter a recall query.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.AbyssCommandOpenQueryUi
                                    }
                                },
                                new DockItem
                                {
                                    Id = "abyssLast",
                                    Type = "button",
                                    Label = "Last Result",
                                    Kind = "ghost",
                                    Tooltip = "Recall the most recent exchange from the latest Truth.log.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.AbyssCommandLast,
                                        RequiresChatId = true
                                    }
                                },
                                new DockItem
                                {
                                    Id = "abyssSessionFiles",
                                    Type = "button",
                                    Label = "Session Files",
                                    Kind = "ghost",
                                    Tooltip = "Open the folder where this session’s memory is stored.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ContinuumCommandOpenSessionFolder,
                                        RequiresChatId = true
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var moduleStatuses = ModuleLoader.GetModuleStatuses().ToList();
            var moduleIssues = moduleStatuses
                .Where(status => !string.Equals(status.Status, "Loaded", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var moduleLoadedCount = moduleStatuses.Count - moduleIssues.Count;

            var advancedSections = new List<DockSection>
            {
                new DockSection
                {
                    Id = "modules",
                    Title = "Modules",
                    Subtitle = moduleIssues.Count > 0
                        ? "Some modules were disabled for safety."
                        : "All modules loaded successfully.",
                    Blocks = moduleIssues.Count > 0
                        ? new List<DockBlock>(
                            moduleIssues.Select(issue => new DockBlock
                            {
                                Type = "hint",
                                ClassName = "valdock-section-hint",
                                Text = $"{issue.Name}: {issue.Status}"
                            }).ToList())
                        : new List<DockBlock>
                        {
                            new DockBlock
                            {
                                Type = "hint",
                                ClassName = "valdock-section-hint",
                                Text = "No disabled modules detected."
                            }
                        }
                },
                new DockSection
                {
                    Id = "privacy",
                    Title = "Data & Privacy",
                    Subtitle = "All VAL data stays on this PC. Use these tools to inspect or clear local data.",
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-row valdock-actions",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "openData",
                                    Type = "button",
                                    Label = "Open Data Folder",
                                    Kind = "ghost",
                                    Tooltip = "Open the local data folder used by VAL.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.PrivacyCommandOpenDataFolder
                                    }
                                },
                                new DockItem
                                {
                                    Id = "wipeData",
                                    Type = "button",
                                    Label = "Wipe Data",
                                    Kind = "danger",
                                    Tooltip = "Wipe local logs, profiles, and session memory. This does not remove the app.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.PrivacyCommandWipeData
                                    }
                                }
                            }
                        }
                    }
                },
                new DockSection
                {
                    Id = "appearance",
                    Title = "Appearance",
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-row",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "void",
                                    Type = "toggle",
                                    Label = "Void",
                                    LocalStateKey = "Void",
                                    Command = new DockCommand
                                    {
                                        Name = "local.void"
                                    }
                                }
                            }
                        },
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-row",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "theme",
                                    Type = "toggle",
                                    Label = "Theme",
                                    LocalStateKey = "Theme",
                                    Command = new DockCommand
                                    {
                                        Name = "local.theme"
                                    }
                                }
                            }
                        }
                    }
                },
                new DockSection
                {
                    Id = "tools",
                    Title = "Tools",
                    Subtitle = "Truth Health & Diagnostics",
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-grid",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "truthHealth",
                                    Type = "button",
                                    Label = "Truth Health",
                                    Kind = "secondary",
                                    Tooltip = "Open the Truth Health report.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ToolsOpenTruthHealth
                                    }
                                },
                                new DockItem
                                {
                                    Id = "diagnostics",
                                    Type = "button",
                                    Label = "Diagnostics",
                                    Kind = "secondary",
                                    Tooltip = "Open diagnostics for the current build.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.ToolsOpenDiagnostics
                                    }
                                }
                            }
                        }
                    }
                },
                new DockSection
                {
                    Id = "navigation",
                    Title = "Navigation",
                    Subtitle = "Return to Chat or step back in history.",
                    Blocks = new List<DockBlock>
                    {
                        new DockBlock
                        {
                            Type = "row",
                            ClassName = "valdock-row valdock-actions",
                            Items = new List<DockItem>
                            {
                                new DockItem
                                {
                                    Id = "navChat",
                                    Type = "button",
                                    Label = "Chat",
                                    Kind = "secondary",
                                    Tooltip = "Return to the main Chat UI.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.NavCommandGoChat
                                    }
                                },
                                new DockItem
                                {
                                    Id = "navBack",
                                    Type = "button",
                                    Label = "Back",
                                    Kind = "ghost",
                                    Tooltip = "Go back to the previous page if available.",
                                    Command = new DockCommand
                                    {
                                        Name = WebCommandNames.NavCommandGoBack
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var statusText = string.IsNullOrWhiteSpace(chatId) ? "Current Session Id: unknown" : $"Current Session Id: {chatId}";
            if (moduleStatuses.Count > 0)
            {
                var issueCount = moduleIssues.Count;
                var moduleSummary = issueCount > 0
                    ? $"Modules: {moduleLoadedCount} loaded, {issueCount} disabled. See Advanced > Modules."
                    : $"Modules: {moduleLoadedCount} loaded.";
                statusText = $"{statusText} · {moduleSummary}";
            }

            return new DockModel
            {
                Version = "1",
                PortalBadge = new DockBadge
                {
                    Label = portalLabel,
                    Count = _portalCount > 0 ? _portalCount : null,
                    Active = portalActive
                },
                Sections = sections,
                AdvancedSections = advancedSections,
                Status = new DockStatus
                {
                    Text = statusText
                }
            };
        }
    }
}
