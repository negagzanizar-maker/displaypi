using System.Net;
using System.Text.Json;

namespace DisplayControl.Api.Notifications;

public static class PasswordResetMessageFactory
{
    public static NotificationMail Create(
        Uri publicBaseUri,
        string recipientEmail,
        string protectedPayloadPlaintext)
    {
        var payload = JsonSerializer.Deserialize<PasswordResetPayload>(protectedPayloadPlaintext)
            ?? throw new InvalidOperationException("Password-reset notification payload is invalid.");
        if (string.IsNullOrWhiteSpace(payload.Token) || payload.Token.Length > 4096)
        {
            throw new InvalidOperationException("Password-reset notification token is invalid.");
        }

        var relative = $"#/reset-password?email={Uri.EscapeDataString(recipientEmail)}&token={Uri.EscapeDataString(payload.Token)}";
        var resetUri = new Uri(publicBaseUri, relative);
        var encodedUri = WebUtility.HtmlEncode(resetUri.AbsoluteUri);
        return new NotificationMail(
            recipientEmail,
            "Réinitialisation de votre mot de passe Display Control",
            $"Une réinitialisation a été demandée. Ouvrez ce lien à usage limité : {resetUri.AbsoluteUri}\n\nSi vous n’êtes pas à l’origine de cette demande, ignorez ce message.",
            $"<p>Une réinitialisation a été demandée.</p><p><a href=\"{encodedUri}\">Choisir un nouveau mot de passe</a></p><p>Si vous n’êtes pas à l’origine de cette demande, ignorez ce message.</p>");
    }

    private sealed record PasswordResetPayload(string Token);
}

public static class InvitationMessageFactory
{
    public static NotificationMail Create(
        Uri publicBaseUri,
        string recipientEmail,
        string protectedPayloadPlaintext)
    {
        var payload = JsonSerializer.Deserialize<InvitationPayload>(protectedPayloadPlaintext)
            ?? throw new InvalidOperationException("Invitation notification payload is invalid.");
        if (string.IsNullOrWhiteSpace(payload.Token) || payload.Token.Length > 4096)
        {
            throw new InvalidOperationException("Invitation notification token is invalid.");
        }

        var invitationUri = new Uri(
            publicBaseUri,
            $"#/accept-invitation?token={Uri.EscapeDataString(payload.Token)}");
        var encodedUri = WebUtility.HtmlEncode(invitationUri.AbsoluteUri);
        return new NotificationMail(
            recipientEmail,
            "Invitation à rejoindre Display Control",
            $"Vous avez été invité à rejoindre Display Control. Ouvrez ce lien à usage unique : {invitationUri.AbsoluteUri}\n\nSi vous n’attendiez pas cette invitation, ignorez ce message.",
            $"<p>Vous avez été invité à rejoindre Display Control.</p><p><a href=\"{encodedUri}\">Créer mon compte</a></p><p>Si vous n’attendiez pas cette invitation, ignorez ce message.</p>");
    }

    private sealed record InvitationPayload(string Token);
}

public sealed record NotificationMail(string RecipientEmail, string Subject, string PlainText, string Html);
