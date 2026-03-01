using UnityEngine;
using TMPro;    

public struct SensorData
{
    public double CO2;
    public double O2;
    public double CO;
    public double HCN;
    public double time;

    public double FEDresult;
}



public class FEDManager : MonoBehaviour
{
    [Header("Input Fields")]
    public TMP_InputField CO2InputField;
    public TMP_InputField COInputField;
    public TMP_InputField O2InputField;
    public TMP_InputField HCNInputField;
    public TMP_InputField TimeInputField;

    [Header("Display")]
    public TextMeshProUGUI FEDResult;
    public TextMeshProUGUI FEDState;

    SensorData data;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    void Start()
    {
        setInitial(ref data);
    }

    // Update is called once per frame
    void Update()
    { 
        UpdateSensorData(ref data);
        CalcFED(ref data);
        DisplayResults();
        GetTenabilityStatus(data.FEDresult);
    }
    void setInitial(ref SensorData data)
    {
        data.CO2 = 0.04;
        data.CO = 0;
        data.O2 = 21;
        data.HCN = 0;
        data.time = 0;
    }
    void UpdateSensorData(ref SensorData data)
    {
        if (double.TryParse(CO2InputField.text, out double co2))
            data.CO2 = co2;
        if (double.TryParse(COInputField.text, out double co))
            data.CO = co;
        if (double.TryParse(O2InputField.text, out double o2))
            data.O2 = o2;
        if (double.TryParse(HCNInputField.text, out double hcn))
            data.HCN = hcn;
        if (double.TryParse(TimeInputField.text, out double time))
            data.time = time;

    }
    void CalcFED(ref SensorData data)
    {
        double vCO2 = (System.Math.Exp(data.CO2 * 0.14) + 1.0) / 2.0;

        // CO에 대한 FED
        double FEDCO = (data.CO * vCO2 * data.time) / 35000.0;

        // HCN에 대한 FED
        double FEDHCN = (System.Math.Pow(data.HCN, 2.36) * data.time) / 1.2e6;

        // O2 결핍에 대한 FED (ISO 근사식 사용)
        double FEDO2 = data.time / (60.0 * System.Math.Exp(6.1 - 0.5189 * data.CO2));

        // 최종 FED 계산
        data.FEDresult = FEDCO + FEDHCN + FEDO2;
    }

    void DisplayResults()
    {

        FEDResult.text = $"{data.FEDresult:F2}";

    }

    string GetTenabilityStatus(double fedValue)
    {
        if (fedValue >= 1.0)
        {
            if (FEDState != null) FEDState.text = "LETHAL";
            return "LETHAL";
        }
        else if (fedValue >= 0.3)
        {
            if (FEDState != null) FEDState.text = "ESCAPE LIMIT";
            return "ESCAPE LIMIT";
        }
        else
        {
            if (FEDState != null) FEDState.text = "SAFE";
            return "SAFE";
        }
    }
}
