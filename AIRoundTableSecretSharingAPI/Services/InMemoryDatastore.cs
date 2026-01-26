
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;
public class InMemoryDataStore
{
    private readonly List<ProducerInfo> _producers = new();
    private readonly List<ProducerEpoch> _epochs = new();
    private readonly List<MetricSubmission> _submissions = new();
    private readonly object _lock = new();
    
    public void SeedData()
    {
        lock (_lock)
        {
            // Add initial producers
            var startDate = new DateTime(2025, 1, 1);
            
            _producers.AddRange(new[]
            {
                new ProducerInfo
                {
                    ProducerId = "partnerA",
                    DisplayName = "Partner A",
                    JoinedDate = startDate,
                    IsActive = true
                },
                new ProducerInfo
                {
                    ProducerId = "partnerB",
                    DisplayName = "Partner B",
                    JoinedDate = startDate,
                    IsActive = true
                },
                new ProducerInfo
                {
                    ProducerId = "partnerC",
                    DisplayName = "Partner C",
                    JoinedDate = startDate,
                    IsActive = true
                }
            });
            
            // Create initial epoch
            _epochs.Add(new ProducerEpoch
            {
                EpochId = 1,
                StartDate = startDate,
                EndDate = null,
                ProducerIds = new List<string> { "partnerA", "partnerB", "partnerC" },
                ProducerCount = 3
            });
            
            Console.WriteLine("Seeded 3 producers and initial epoch");
        }
    }
    
    public List<ProducerInfo> GetActiveProducers(DateTime effectiveDate)
    {
        lock (_lock)
        {
            return _producers
                .Where(p => p.JoinedDate <= effectiveDate && p.IsActive)
                .OrderBy(p => p.ProducerId)
                .ToList();
        }
    }
    
    public ProducerEpoch GetEpochForDate(DateTime date)
    {
        lock (_lock)
        {
            return _epochs
                .Where(e => e.StartDate <= date && (e.EndDate == null || e.EndDate > date))
                .OrderByDescending(e => e.StartDate)
                .FirstOrDefault();
        }
    }
    
    public bool AddSubmission(MetricSubmission submission)
    {
        lock (_lock)
        {
            // Check for duplicate
            var exists = _submissions.Any(s =>
                s.ProducerId == submission.ProducerId &&
                s.Country == submission.Country &&
                s.Month == submission.Month);
            
            if (exists)
                return false;
            
            _submissions.Add(submission);
            return true;
        }
    }
    
    public List<MetricSubmission> GetSubmissions(string country, DateTime month, int epochId)
    {
        lock (_lock)
        {
            return _submissions
                .Where(s => s.Country == country && s.Month == month && s.EpochId == epochId)
                .ToList();
        }
    }
    
    public void AddProducer(ProducerInfo producer)
    {
        lock (_lock)
        {
            _producers.Add(producer);
        }
    }
    
    public void CreateEpoch(ProducerEpoch epoch)
    {
        lock (_lock)
        {
            // End current epoch
            var currentEpoch = _epochs.FirstOrDefault(e => e.EndDate == null);
            if (currentEpoch != null)
            {
                currentEpoch.EndDate = epoch.StartDate;
            }
            
            _epochs.Add(epoch);
        }
    }
}