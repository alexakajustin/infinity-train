using UnityEngine;
using System.Collections;
using System.Threading.Tasks;
using System.Threading;

public class SFXManager : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private AudioSource owlsHowl;
    [SerializeField] private AudioSource wolvesHowl;
    
    [Header("Owls Howl Settings")]
    [SerializeField] private float owlMinInterval = 10f;
    [SerializeField] private float owlMaxInterval = 30f;
    [SerializeField] private bool owlsEnabled = true;
    
    [Header("Wolves Howl Settings")]
    [SerializeField] private float wolvesMinInterval = 15f;
    [SerializeField] private float wolvesMaxInterval = 45f;
    [SerializeField] private bool wolvesEnabled = true;
    
    [Header("General Settings")]
    [SerializeField] private Vector3 positionOffset = Vector3.up * 3f;
    [SerializeField] private bool playOnStart = true;
    
    // Cancellation tokens for controlling async operations
    private CancellationTokenSource owlsCancellationToken;
    private CancellationTokenSource wolvesCancellationToken;
    
    // Status tracking
    private bool isPlayingOwls = false;
    private bool isPlayingWolves = false;
    
    [Header("Debug Info (Read Only)")]
    [SerializeField] private float nextOwlTime;
    [SerializeField] private float nextWolvesTime;

    private void Start()
    {
        if (playOnStart)
        {
            StartAllSFX();
        }
    }

    private void Update()
    {
        // Update sfx manager position to player position + offset
        if (player != null)
        {
            transform.position = player.position + positionOffset;
        }
    }

    private void OnDestroy()
    {
        StopAllSFX();
    }

    private void OnDisable()
    {
        StopAllSFX();
    }

    public void StartAllSFX()
    {
        if (owlsEnabled && owlsHowl != null)
        {
            StartOwlsSFX();
        }
        
        if (wolvesEnabled && wolvesHowl != null)
        {
            StartWolvesSFX();
        }
    }

    public void StopAllSFX()
    {
        StopOwlsSFX();
        StopWolvesSFX();
    }

    public void StartOwlsSFX()
    {
        if (owlsHowl == null || isPlayingOwls) return;
        
        owlsCancellationToken?.Cancel();
        owlsCancellationToken = new CancellationTokenSource();
        
        _ = PlayOwlsSFXAsync(owlsCancellationToken.Token);
    }

    public void StopOwlsSFX()
    {
        owlsCancellationToken?.Cancel();
        owlsCancellationToken?.Dispose();
        owlsCancellationToken = null;
        isPlayingOwls = false;
        
        if (owlsHowl != null && owlsHowl.isPlaying)
        {
            owlsHowl.Stop();
        }
    }

    public void StartWolvesSFX()
    {
        if (wolvesHowl == null || isPlayingWolves) return;
        
        wolvesCancellationToken?.Cancel();
        wolvesCancellationToken = new CancellationTokenSource();
        
        _ = PlayWolvesSFXAsync(wolvesCancellationToken.Token);
    }

    public void StopWolvesSFX()
    {
        wolvesCancellationToken?.Cancel();
        wolvesCancellationToken?.Dispose();
        wolvesCancellationToken = null;
        isPlayingWolves = false;
        
        if (wolvesHowl != null && wolvesHowl.isPlaying)
        {
            wolvesHowl.Stop();
        }
    }

    private async Task PlayOwlsSFXAsync(CancellationToken cancellationToken)
    {
        isPlayingOwls = true;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && this != null)
            {
                // Calculate random interval
                float waitTime = Random.Range(owlMinInterval, owlMaxInterval);
                nextOwlTime = Time.time + waitTime;
                
                // Wait for the interval
                await WaitForSecondsAsync(waitTime, cancellationToken);
                
                // Check if we're still valid and not cancelled
                if (cancellationToken.IsCancellationRequested || this == null || owlsHowl == null)
                    break;
                
                // Play the sound effect
                if (owlsEnabled && !owlsHowl.isPlaying)
                {
                    owlsHowl.Play();
                }
            }
        }
        catch (System.OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in OwlsSFX: {e.Message}");
        }
        finally
        {
            isPlayingOwls = false;
        }
    }

    private async Task PlayWolvesSFXAsync(CancellationToken cancellationToken)
    {
        isPlayingWolves = true;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && this != null)
            {
                // Calculate random interval
                float waitTime = Random.Range(wolvesMinInterval, wolvesMaxInterval);
                nextWolvesTime = Time.time + waitTime;
                
                // Wait for the interval
                await WaitForSecondsAsync(waitTime, cancellationToken);
                
                // Check if we're still valid and not cancelled
                if (cancellationToken.IsCancellationRequested || this == null || wolvesHowl == null)
                    break;
                
                // Play the sound effect
                if (wolvesEnabled && !wolvesHowl.isPlaying)
                {
                    wolvesHowl.Play();
                }
            }
        }
        catch (System.OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in WolvesSFX: {e.Message}");
        }
        finally
        {
            isPlayingWolves = false;
        }
    }

    private async Task WaitForSecondsAsync(float seconds, CancellationToken cancellationToken)
    {
        float endTime = Time.time + seconds;
        
        while (Time.time < endTime && !cancellationToken.IsCancellationRequested && this != null)
        {
            await Task.Yield(); // Yield control back to Unity's main thread
        }
        
        if (cancellationToken.IsCancellationRequested)
        {
            throw new System.OperationCanceledException();
        }
    }

    // Public methods for external control
    public void SetOwlsEnabled(bool enabled)
    {
        owlsEnabled = enabled;
        if (!enabled)
        {
            StopOwlsSFX();
        }
        else if (enabled && !isPlayingOwls)
        {
            StartOwlsSFX();
        }
    }

    public void SetWolvesEnabled(bool enabled)
    {
        wolvesEnabled = enabled;
        if (!enabled)
        {
            StopWolvesSFX();
        }
        else if (enabled && !isPlayingWolves)
        {
            StartWolvesSFX();
        }
    }

    public void SetOwlInterval(float min, float max)
    {
        owlMinInterval = Mathf.Max(0.1f, min);
        owlMaxInterval = Mathf.Max(owlMinInterval, max);
    }

    public void SetWolvesInterval(float min, float max)
    {
        wolvesMinInterval = Mathf.Max(0.1f, min);
        wolvesMaxInterval = Mathf.Max(wolvesMinInterval, max);
    }

    // Coroutine alternative (if you prefer coroutines over async/await)
    public void StartOwlsSFXCoroutine()
    {
        StopCoroutine(nameof(OwlsSFXCoroutine));
        StartCoroutine(OwlsSFXCoroutine());
    }

    public void StartWolvesSFXCoroutine()
    {
        StopCoroutine(nameof(WolvesSFXCoroutine));
        StartCoroutine(WolvesSFXCoroutine());
    }

    private IEnumerator OwlsSFXCoroutine()
    {
        while (owlsEnabled && owlsHowl != null)
        {
            float waitTime = Random.Range(owlMinInterval, owlMaxInterval);
            yield return new WaitForSeconds(waitTime);
            
            if (owlsEnabled && owlsHowl != null && !owlsHowl.isPlaying)
            {
                owlsHowl.Play();
            }
        }
    }

    private IEnumerator WolvesSFXCoroutine()
    {
        while (wolvesEnabled && wolvesHowl != null)
        {
            float waitTime = Random.Range(wolvesMinInterval, wolvesMaxInterval);
            yield return new WaitForSeconds(waitTime);
            
            if (wolvesEnabled && wolvesHowl != null && !wolvesHowl.isPlaying)
            {
                wolvesHowl.Play();
            }
        }
    }
}