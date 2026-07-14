using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using VAL.Contracts;

namespace VAL.App.Host.Services
{
    public static class DockModelBuilder
    {
        public static DockModel Build(DockModelSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var moduleStatuses = snapshot.ModuleStatuses.ToList();
            var moduleIssues = moduleStatuses
                .Where(status => !string.Equals(status.Status, "Loaded", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var moduleLoadedCount = moduleStatuses.Count - moduleIssues.Count;

            return new DockModel
            {
                Version = "1",
                PortalBadge = BuildPortalBadge(snapshot),
                Sections = BuildPrimarySections(snapshot),
                AdvancedSections = BuildAdvancedSections(moduleIssues),
                Status = new DockStatus
                {
                    Text = BuildStatusText(snapshot.ChatId, moduleStatuses.Count, moduleLoadedCount, moduleIssues.Count)
                }
            };
        }

        private static DockBadge BuildPortalBadge(DockModelSnapshot snapshot)
        {
            var portalActive = snapshot.PortalEnabled || snapshot.PortalCount > 0;
            return new DockBadge
            {
                Label = snapshot.PortalEnabled ? "Portal Armed" : "Portal",
                Count = snapshot.PortalCount > 0 ? snapshot.PortalCount : null,
                Active = portalActive
            };
        }

        private static IReadOnlyList<DockSection> BuildPrimarySections(DockModelSnapshot snapshot)
        {
            return
            [
                BuildContinuumSection(snapshot.ContinuumLoggingEnabled),
                BuildPortalSection(snapshot),
                BuildAbyssSection()
            ];
        }

        private static IReadOnlyList<DockSection> BuildAdvancedSections(IReadOnlyList<VAL.Host.Services.ModuleStatusInfo> moduleIssues)
        {
            return
            [
                BuildModulesSection(moduleIssues),
                BuildPrivacySection(),
                BuildAppearanceSection(),
                BuildToolsSection(),
                BuildNavigationSection()
            ];
        }

        private static string BuildStatusText(string? chatId, int moduleCount, int moduleLoadedCount, int moduleIssueCount)
        {
            var statusText = string.IsNullOrWhiteSpace(chatId)
                ? "Current Session Id: unknown"
                : $"Current Session Id: {chatId}";

            if (moduleCount <= 0)
            {
                return statusText;
            }

            var moduleSummary = moduleIssueCount > 0
                ? $"Modules: {moduleLoadedCount} loaded, {moduleIssueCount} disabled. See Advanced > Modules."
                : $"Modules: {moduleLoadedCount} loaded.";
            return $"{statusText} | {moduleSummary}";
        }

        private static DockSection BuildContinuumSection(bool continuumLoggingEnabled)
        {
            return new DockSection
            {
                Id = "continuum",
                Title = "Continuum",
                HeaderControl = new DockItem
                {
                    Id = "continuumLogging",
                    Type = "toggle",
                    Label = "Continuum logging",
                    State = continuumLoggingEnabled,
                    Tooltip = "Allow Continuum to write Truth.log for this session.",
                    Command = new DockCommand
                    {
                        Name = WebCommandNames.PrivacyCommandSetContinuumLogging
                    }
                },
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-grid",
                        Items =
                        [
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
                                Id = "chronicle",
                                Type = "button",
                                Label = "Chronicle",
                                Kind = "ghost",
                                Tooltip = "Scan the current chat and rebuild VAL's memory for this session.",
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
                                Tooltip = "Open the folder on your computer where this session's files are stored.",
                                Command = new DockCommand
                                {
                                    Name = WebCommandNames.ContinuumCommandOpenSessionFolder,
                                    RequiresChatId = true
                                }
                            }
                        ]
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
                ]
            };
        }

        private static DockSection BuildPortalSection(DockModelSnapshot snapshot)
        {
            var portalSubtitle = snapshot.PortalPrivacyAllowed ? "Hotkeys enabled" : "Disabled by Privacy setting";
            var portalSendHint = snapshot.PortalCount <= 0
                ? "Stage at least one capture to enable Send."
                : "Send will paste all staged captures into the composer.";
            var portalToggleDisabled = !snapshot.PortalPrivacyAllowed;
            var portalToggleReason = portalToggleDisabled ? "Disabled by Privacy setting." : null;
            var portalSendDisabled = !snapshot.PortalPrivacyAllowed || !snapshot.PortalEnabled || snapshot.PortalCount <= 0;
            string? portalSendReason = null;
            if (!snapshot.PortalPrivacyAllowed)
            {
                portalSendReason = "Portal capture is disabled in Privacy settings.";
            }
            else if (!snapshot.PortalEnabled)
            {
                portalSendReason = "Arm Capture & Stage to enable Send.";
            }
            else if (snapshot.PortalCount <= 0)
            {
                portalSendReason = "Stage at least one capture to enable Send.";
            }

            return new DockSection
            {
                Id = "portal",
                Title = "Portal",
                Subtitle = portalSubtitle,
                HeaderControl = new DockItem
                {
                    Id = "portalPrivacy",
                    Type = "toggle",
                    Label = "Portal capture & hotkeys",
                    State = snapshot.PortalCaptureEnabled,
                    Tooltip = "Allow Portal to register hotkeys and monitor the clipboard.",
                    Command = new DockCommand
                    {
                        Name = WebCommandNames.PrivacyCommandSetPortalCapture
                    }
                },
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-inline-row",
                        Items =
                        [
                            new DockItem
                            {
                                Id = "portalToggle",
                                Type = "toggle",
                                Label = "Capture & Stage",
                                State = snapshot.PortalEnabled,
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
                                Count = snapshot.PortalCount,
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
                        ]
                    },
                    new DockBlock
                    {
                        Id = "portalSendHint",
                        Type = "hint",
                        ClassName = "valdock-section-hint valdock-status-hint",
                        Text = portalSendHint
                    }
                ]
            };
        }

        private static DockSection BuildAbyssSection()
        {
            return new DockSection
            {
                Id = "abyss",
                Title = "Abyss",
                Subtitle = "Recall & search",
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-row valdock-actions",
                        Items =
                        [
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
                                Tooltip = "Open the folder where this session's memory is stored.",
                                Command = new DockCommand
                                {
                                    Name = WebCommandNames.ContinuumCommandOpenSessionFolder,
                                    RequiresChatId = true
                                }
                            }
                        ]
                    }
                ]
            };
        }

        private static DockSection BuildModulesSection(IReadOnlyList<VAL.Host.Services.ModuleStatusInfo> moduleIssues)
        {
            return new DockSection
            {
                Id = "modules",
                Title = "Modules",
                Subtitle = moduleIssues.Count > 0
                    ? "Some modules were disabled for safety."
                    : "All modules loaded successfully.",
                Blocks = moduleIssues.Count > 0
                    ? moduleIssues.Select(issue => new DockBlock
                    {
                        Type = "hint",
                        ClassName = "valdock-section-hint",
                        Text = $"{issue.Name}: {issue.Status}"
                    }).ToList()
                    :
                    [
                        new DockBlock
                        {
                            Type = "hint",
                            ClassName = "valdock-section-hint",
                            Text = "No disabled modules detected."
                        }
                    ]
            };
        }

        private static DockSection BuildPrivacySection()
        {
            return new DockSection
            {
                Id = "privacy",
                Title = "Data & Privacy",
                Subtitle = "All VAL data stays on this PC. Use these tools to inspect or clear local data.",
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-row valdock-actions",
                        Items =
                        [
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
                        ]
                    }
                ]
            };
        }

        private static DockSection BuildAppearanceSection()
        {
            return new DockSection
            {
                Id = "appearance",
                Title = "Appearance",
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-row",
                        Items =
                        [
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
                        ]
                    },
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-row",
                        Items =
                        [
                            new DockItem
                            {
                                Id = "voidInsertNoCodeBlocks",
                                Type = "button",
                                Label = "Insert: No code blocks",
                                Kind = "secondary",
                                Tooltip = "Append a short note to the composer asking for plain-text responses.",
                                Command = new DockCommand
                                {
                                    Name = "local.void_insert_no_code_blocks"
                                }
                            },
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
                        ]
                    }
                ]
            };
        }

        private static DockSection BuildToolsSection()
        {
            return new DockSection
            {
                Id = "tools",
                Title = "Tools",
                Subtitle = "Truth Health & Diagnostics",
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-grid",
                        Items =
                        [
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
                        ]
                    }
                ]
            };
        }

        private static DockSection BuildNavigationSection()
        {
            return new DockSection
            {
                Id = "navigation",
                Title = "Navigation",
                Subtitle = "Return to Chat or step back in history.",
                Blocks =
                [
                    new DockBlock
                    {
                        Type = "row",
                        ClassName = "valdock-row valdock-actions",
                        Items =
                        [
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
                        ]
                    }
                ]
            };
        }
    }
}
