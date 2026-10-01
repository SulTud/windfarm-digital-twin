using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// About panel: what the demo is, the swappable data source, the fault to try, and the credits with links
    /// (GitHub repository, LinkedIn, e-mail). Opened by the (i) button in the top bar and by the small credit line
    /// ("by ..." next to the data source badge on PC, on the More page on phones); closed by its close button or a tap
    /// on the dimmed backdrop.
    ///
    /// The panel is a sibling after #dashboard in the UXML, so it covers everything incl. the phone sheet. The texts
    /// live in the UXML (the name with XML character references, so the files stay ASCII); the URLs come from the
    /// DashboardController Inspector.
    /// </summary>
    internal sealed class AboutPresenter
    {
        private const string OpenClass = "about--open";

        private readonly VisualElement overlay;
        private readonly Button openButton;
        private readonly Label creditLine;
        private readonly Button closeButton;
        private readonly ExternalLink github;
        private readonly ExternalLink linkedIn;
        private readonly ExternalLink emailLink;
        private readonly CopyButton emailCopy;

        public AboutPresenter(VisualElement root, string githubUrl, string linkedInUrl, string emailAddress)
        {
            overlay = root.Require<VisualElement>("about");
            openButton = root.Require<Button>("about-button");
            creditLine = root.Require<Label>("credit-line");
            closeButton = root.Require<Button>("about-close");

            github = new ExternalLink(root.Require<Button>("about-github"), githubUrl);
            linkedIn = new ExternalLink(root.Require<Button>("about-linkedin"), linkedInUrl);

            // Phones open their mail app from mailto:. On desktops mailto: often opens an empty browser tab (no mail
            // app set up, webmail users), so the button copies the address there instead.
            Button emailButton = root.Require<Button>("about-email");
            if (Application.isMobilePlatform)
                emailLink = new ExternalLink(emailButton, string.IsNullOrEmpty(emailAddress) ? null : "mailto:" + emailAddress);
            else
            {
                emailButton.text = "COPY E-MAIL";
                emailCopy = new CopyButton(emailButton, emailAddress);
            }

            root.Require<Label>("about-email-address").text = emailAddress;

            openButton.clicked += Open;
            closeButton.clicked += Close;
            creditLine.RegisterCallback<ClickEvent>(HandleCreditClicked);
            overlay.RegisterCallback<ClickEvent>(HandleOverlayClicked);
        }

        public void Dispose()
        {
            openButton.clicked -= Open;
            closeButton.clicked -= Close;
            creditLine.UnregisterCallback<ClickEvent>(HandleCreditClicked);
            overlay.UnregisterCallback<ClickEvent>(HandleOverlayClicked);
            github.Dispose();
            linkedIn.Dispose();
            emailLink?.Dispose();
            emailCopy?.Dispose();
            Close();
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime) => emailCopy?.Tick(deltaTime);

        private void Open() => overlay.AddToClassList(OpenClass);

        private void Close() => overlay.RemoveFromClassList(OpenClass);

        private void HandleCreditClicked(ClickEvent evt) => Open();

        // Only a tap on the dimmed backdrop itself closes; taps on the card bubble up with another target.
        private void HandleOverlayClicked(ClickEvent evt)
        {
            if (evt.target == overlay)
                Close();
        }
    }
}
