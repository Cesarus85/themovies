using System;
using TheMovies.Core;
using TMPro;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>A movie or folder in the tile view: poster (or a placeholder with the title), title, details and progress.</summary>
    public sealed class PosterTile
    {
        public const float Width = 0.135f;
        public const float Height = 0.2f;
        const float k_PosterWidth = 0.119f;
        const float k_PosterHeight = 0.15f;
        const float k_Margin = 0.008f;
        const float k_TextWidth = Width - 0.014f;
        const float k_TitleHeight = 0.0118f;
        const float k_DetailsHeight = 0.0098f;
        const float k_PlaceholderHeight = 0.0125f;

        static Material s_ImageMaterial;
        static readonly int k_MainTex = Shader.PropertyToID("_MainTex");
        static readonly int k_MainTexSt = Shader.PropertyToID("_MainTex_ST");
        static readonly int k_Size = Shader.PropertyToID("_Size");
        static readonly int k_Radius = Shader.PropertyToID("_Radius");
        static readonly int k_Color = Shader.PropertyToID("_Color");

        readonly UIButton m_Button;
        readonly MeshRenderer m_Poster;
        readonly MeshRenderer m_Placeholder;
        readonly MeshRenderer m_PlaceholderIcon;
        readonly TextMeshPro m_PlaceholderTitle;
        readonly TextMeshPro m_Title;
        readonly TextMeshPro m_Details;
        readonly MeshRenderer m_ProgressBack;
        readonly MeshRenderer m_ProgressFill;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        Texture m_Texture;
        string m_TitleText;
        string m_DetailsText;
        string m_PlaceholderText;

        public GameObject gameObject => m_Button.gameObject;

        static Material ImageMaterial
        {
            get
            {
                if (s_ImageMaterial == null)
                    s_ImageMaterial = new Material(UIFactory.FindShader("TheMovies/UIImage")) { renderQueue = 2993 };
                return s_ImageMaterial;
            }
        }

        public PosterTile(Transform parent, Vector3 localPosition, Action onClick)
        {
            m_Button = UIButton.Create(parent, null, localPosition, new Vector2(Width, Height), onClick, 0.012f);
            var t = m_Button.transform;
            float posterY = Height * 0.5f - k_Margin - k_PosterHeight * 0.5f;
            var posterPos = new Vector3(0, posterY, -UIStyle.Layer * 0.5f);

            m_Placeholder = UIFactory.CreateRounded(t, "Placeholder", posterPos, new Vector2(k_PosterWidth, k_PosterHeight), 0.006f,
                UIStyle.PanelInset, UIFactory.WidgetMaterial, 0.3f);
            m_Placeholder.transform.localPosition = posterPos + new Vector3(0, 0, -UIStyle.Layer * 0.25f);
            m_PlaceholderIcon = UIIcons.Create(t, UIIcon.Film, posterPos + new Vector3(0, 0.045f, -UIStyle.Layer), 0.03f, UIStyle.TextDim * 0.8f);
            m_PlaceholderTitle = UIFactory.CreateText(t, string.Empty, posterPos + new Vector3(0, -0.012f, -UIStyle.Layer), k_PlaceholderHeight,
                TextAnchor.MiddleCenter, UIStyle.Text);

            m_Poster = UIFactory.CreateQuad(t, "Poster", posterPos + new Vector3(0, 0, -UIStyle.Layer * 0.75f),
                new Vector2(k_PosterWidth, k_PosterHeight), Color.white, ImageMaterial);
            m_Poster.gameObject.SetActive(false);

            float barY = posterY - k_PosterHeight * 0.5f + 0.006f;
            m_ProgressBack = UIFactory.CreateQuad(t, "Progress", new Vector3(0, barY, -UIStyle.Layer * 1.2f),
                new Vector2(k_PosterWidth - 0.016f, 0.004f), new Color(0, 0, 0, 0.7f), UIFactory.OverlayMaterial);
            m_ProgressFill = UIFactory.CreateQuad(t, "Progress Fill", new Vector3(0, barY, -UIStyle.Layer * 1.4f),
                new Vector2(k_PosterWidth - 0.016f, 0.004f), UIStyle.Accent, UIFactory.OverlayMaterial);

            float textTop = posterY - k_PosterHeight * 0.5f;
            m_Title = UIFactory.CreateText(t, string.Empty, new Vector3(0, textTop - 0.0115f, -UIStyle.Layer), k_TitleHeight, TextAnchor.MiddleCenter);
            m_Details = UIFactory.CreateText(t, string.Empty, new Vector3(0, textTop - 0.0255f, -UIStyle.Layer), k_DetailsHeight,
                TextAnchor.MiddleCenter, UIStyle.TextDim);
            SetProgress(-1);
        }

        public bool Visible
        {
            get => m_Button.gameObject.activeSelf;
            set
            {
                if (m_Button.gameObject.activeSelf != value) m_Button.gameObject.SetActive(value);
            }
        }

        /// <param name="progress">0..1 for a started movie, negative to hide the bar.</param>
        public void Set(string title, string details, Texture poster, bool folder, float progress)
        {
            title = title ?? string.Empty;
            details = details ?? string.Empty;
            string placeholder = poster != null ? string.Empty : title;
            // Text is fitted with the real glyph widths only when it changes (the menu refreshes often).
            if (m_TitleText != title)
            {
                m_TitleText = title;
                m_Title.text = TextFit.Ellipsize(title, k_TextWidth, UIFactory.CharWidth(title, k_TitleHeight));
            }
            if (m_DetailsText != details)
            {
                m_DetailsText = details;
                m_Details.text = TextFit.Ellipsize(details, k_TextWidth, UIFactory.CharWidth(details, k_DetailsHeight));
            }
            SetPoster(poster);
            if (m_PlaceholderText != placeholder)
            {
                m_PlaceholderText = placeholder;
                m_PlaceholderTitle.text = TextFit.Wrap(placeholder, k_PosterWidth - 0.014f, 5, UIFactory.CharWidth(placeholder, k_PlaceholderHeight));
            }
            m_PlaceholderIcon.gameObject.SetActive(poster == null && !folder);
            m_Placeholder.gameObject.SetActive(poster == null);
            UIFactory.SetColor(m_Placeholder, folder ? new Color(0.16f, 0.15f, 0.12f, 0.95f) : UIStyle.PanelInset);
            SetProgress(progress);
        }

        void SetPoster(Texture texture)
        {
            if (texture == m_Texture) return;
            m_Texture = texture;
            m_Poster.gameObject.SetActive(texture != null);
            if (texture == null) return;
            // Fill the frame, cropping what sticks out (landscape library pictures, odd poster sizes).
            float frame = k_PosterWidth / k_PosterHeight;
            float image = texture.width / (float)Mathf.Max(1, texture.height);
            Vector4 st = image > frame
                ? new Vector4(frame / image, 1, (1 - frame / image) * 0.5f, 0)
                : new Vector4(1, image / frame, 0, (1 - image / frame) * 0.5f);
            m_Poster.GetPropertyBlock(m_Block);
            m_Block.SetTexture(k_MainTex, texture);
            m_Block.SetVector(k_MainTexSt, st);
            m_Block.SetVector(k_Size, new Vector4(k_PosterWidth, k_PosterHeight, 0, 0));
            m_Block.SetFloat(k_Radius, 0.006f);
            m_Block.SetColor(k_Color, Color.white);
            m_Poster.SetPropertyBlock(m_Block);
        }

        void SetProgress(float progress)
        {
            bool show = progress >= 0;
            m_ProgressBack.gameObject.SetActive(show);
            m_ProgressFill.gameObject.SetActive(show);
            if (!show) return;
            float full = k_PosterWidth - 0.016f;
            float w = Mathf.Max(0.002f, full * Mathf.Clamp01(progress));
            var tr = m_ProgressFill.transform;
            tr.localScale = new Vector3(w, tr.localScale.y, 1);
            var p = tr.localPosition;
            tr.localPosition = new Vector3(-full * 0.5f + w * 0.5f, p.y, p.z);
        }
    }
}
