using System.Collections.Generic;
using System.Linq;

using VAL.App.Host.Services;
using VAL.Contracts;
using VAL.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class DockModelBuilderTests
    {
        [Fact]
        public void BuildUsesAsciiCopyAndSummarizesModuleIssues()
        {
            var model = DockModelBuilder.Build(new DockModelSnapshot
            {
                ChatId = "chat-123",
                ContinuumLoggingEnabled = true,
                PortalCaptureEnabled = true,
                PortalEnabled = true,
                PortalPrivacyAllowed = true,
                PortalCount = 2,
                ModuleStatuses =
                [
                    new ModuleStatusInfo("Dock", "Loaded", "dock.module.json"),
                    new ModuleStatusInfo("Abyss", "Disabled", "abyss.module.json")
                ]
            });

            var chronicle = FindItem(model.Sections, "continuum", "chronicle");
            var sessionFiles = FindItem(model.Sections, "continuum", "sessionFiles");
            var statusText = model.Status?.Text;

            Assert.Equal("Scan the current chat and rebuild VAL's memory for this session.", chronicle.Tooltip);
            Assert.Equal("Open the folder on your computer where this session's files are stored.", sessionFiles.Tooltip);
            Assert.Equal("Current Session Id: chat-123 | Modules: 1 loaded, 1 disabled. See Advanced > Modules.", statusText);
            Assert.DoesNotContain(EnumerateCopy(model), value => value.Contains('Â') || value.Contains('â'));
        }

        [Fact]
        public void BuildDisablesPortalActionsWhenPrivacyBlocksCapture()
        {
            var model = DockModelBuilder.Build(new DockModelSnapshot
            {
                PortalCaptureEnabled = false,
                PortalEnabled = true,
                PortalPrivacyAllowed = false,
                PortalCount = 3
            });

            var portalSection = FindSection(model.Sections, "portal");
            var portalToggle = FindItem(model.Sections, "portal", "portalToggle");
            var portalSend = FindItem(model.Sections, "portal", "portalSend");

            Assert.Equal("Disabled by Privacy setting", portalSection.Subtitle);
            Assert.True(portalToggle.Disabled);
            Assert.Equal("Disabled by Privacy setting.", portalToggle.DisabledReason);
            Assert.True(portalSend.Disabled);
            Assert.Equal("Portal capture is disabled in Privacy settings.", portalSend.DisabledReason);
        }

        [Fact]
        public void BuildShowsHealthyModuleSummaryWhenNothingIsDisabled()
        {
            var model = DockModelBuilder.Build(new DockModelSnapshot
            {
                ModuleStatuses =
                [
                    new ModuleStatusInfo("Dock", "Loaded", "dock.module.json"),
                    new ModuleStatusInfo("Portal", "Loaded", "portal.module.json")
                ]
            });

            var modulesSection = FindSection(model.AdvancedSections, "modules");

            Assert.Equal("All modules loaded successfully.", modulesSection.Subtitle);
            Assert.Equal("No disabled modules detected.", modulesSection.Blocks.Single().Text);
            Assert.Equal("Current Session Id: unknown | Modules: 2 loaded.", model.Status?.Text);
        }

        private static DockSection FindSection(IEnumerable<DockSection> sections, string id)
        {
            return sections.Single(section => section.Id == id);
        }

        private static DockItem FindItem(IEnumerable<DockSection> sections, string sectionId, string itemId)
        {
            return FindSection(sections, sectionId)
                .Blocks
                .SelectMany(block => block.Items ?? [])
                .Single(item => item.Id == itemId);
        }

        private static IEnumerable<string> EnumerateCopy(DockModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.Status?.Text))
            {
                yield return model.Status.Text;
            }

            foreach (var section in model.Sections.Concat(model.AdvancedSections))
            {
                if (!string.IsNullOrWhiteSpace(section.Title))
                {
                    yield return section.Title;
                }

                if (!string.IsNullOrWhiteSpace(section.Subtitle))
                {
                    yield return section.Subtitle;
                }

                if (section.HeaderControl != null)
                {
                    foreach (var value in EnumerateItemCopy(section.HeaderControl))
                    {
                        yield return value;
                    }
                }

                foreach (var block in section.Blocks)
                {
                    if (!string.IsNullOrWhiteSpace(block.Text))
                    {
                        yield return block.Text;
                    }

                    foreach (var item in block.Items ?? [])
                    {
                        foreach (var value in EnumerateItemCopy(item))
                        {
                            yield return value;
                        }
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateItemCopy(DockItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Label))
            {
                yield return item.Label;
            }

            if (!string.IsNullOrWhiteSpace(item.Tooltip))
            {
                yield return item.Tooltip;
            }

            if (!string.IsNullOrWhiteSpace(item.DisabledReason))
            {
                yield return item.DisabledReason;
            }
        }
    }
}
